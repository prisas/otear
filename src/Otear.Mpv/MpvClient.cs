using System.Collections.Concurrent;
using System.Globalization;
using Otear.Mpv.Native;

namespace Otear.Mpv;

/// <summary>
/// Owns one libmpv core. All libmpv calls are thread-safe; events are read on a dedicated thread and
/// raised from it, so subscribers must marshal to their UI thread themselves.
/// </summary>
public sealed unsafe class MpvClient : IDisposable
{
    private nint _handle;
    private Thread? _eventThread;
    private volatile bool _disposed;
    private long _nextReplyId = 1;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<object?>> _pending = new();
    private readonly ConcurrentDictionary<ulong, string> _observed = new();

    /// <summary>Property observation results (raised on the event thread).</summary>
    public event Action<MpvPropertyChange>? PropertyChanged;

    /// <summary>Payload-less events: FileLoaded, VideoReconfig, AudioReconfig, PlaybackRestart, Seek, Idle, Shutdown.</summary>
    public event Action<MpvSimpleEvent>? EventRaised;

    public event Action<long>? StartFile;
    public event Action<MpvEndFile>? EndFile;
    public event Action<MpvLogMessage>? LogMessage;
    public event Action<IReadOnlyList<string>>? ClientMessage;

    public static uint ClientApiVersion => LibMpv.mpv_client_api_version();

    public bool IsDisposed => _disposed;

    private MpvClient(nint handle) => _handle = handle;

    /// <summary>Creates and initializes a core with the given options (applied before mpv_initialize).</summary>
    public static MpvClient Create(IEnumerable<KeyValuePair<string, string>> options, string? logLevel = "warn")
    {
        nint handle = LibMpv.mpv_create();
        if (handle == 0) throw new InvalidOperationException("mpv_create failed (out of memory?)");
        var client = new MpvClient(handle);
        try
        {
            foreach (var (name, value) in options)
                client.SetOption(name, value);
            Check(LibMpv.mpv_initialize(handle), "mpv_initialize");
            if (logLevel is not null)
            {
                using var lvl = new Utf8.NativeString(logLevel);
                LibMpv.mpv_request_log_messages(handle, lvl.Pointer);
            }
        }
        catch
        {
            LibMpv.mpv_terminate_destroy(handle);
            client._handle = 0;
            throw;
        }
        client._eventThread = new Thread(client.EventLoop)
        {
            IsBackground = true,
            Name = "mpv events",
            Priority = ThreadPriority.AboveNormal,
        };
        client._eventThread.Start();
        return client;
    }

    // ------------------------------------------------------------------ options & properties

    public void SetOption(string name, string value)
    {
        using var n = new Utf8.NativeString(name);
        using var v = new Utf8.NativeString(value);
        Check(LibMpv.mpv_set_option_string(Handle, n.Pointer, v.Pointer), $"option '{name}={value}'");
    }

    public void SetProperty(string name, string value)
    {
        using var n = new Utf8.NativeString(name);
        using var v = new Utf8.NativeString(value);
        Check(LibMpv.mpv_set_property_string(Handle, n.Pointer, v.Pointer), $"set '{name}'");
    }

    public void SetProperty(string name, bool value)
    {
        int flag = value ? 1 : 0;
        using var n = new Utf8.NativeString(name);
        Check(LibMpv.mpv_set_property(Handle, n.Pointer, MpvFormat.Flag, &flag), $"set '{name}'");
    }

    public void SetProperty(string name, long value)
    {
        using var n = new Utf8.NativeString(name);
        Check(LibMpv.mpv_set_property(Handle, n.Pointer, MpvFormat.Int64, &value), $"set '{name}'");
    }

    public void SetProperty(string name, double value)
    {
        using var n = new Utf8.NativeString(name);
        Check(LibMpv.mpv_set_property(Handle, n.Pointer, MpvFormat.Double, &value), $"set '{name}'");
    }

    /// <summary>Sets a property without blocking; errors are reported through the returned task.</summary>
    public Task SetPropertyAsync(string name, string value)
    {
        var (id, tcs) = NewRequest();
        using var n = new Utf8.NativeString(name);
        using var v = new Utf8.NativeString(value);
        byte* p = v.Pointer;
        int err = LibMpv.mpv_set_property_async(Handle, id, n.Pointer, MpvFormat.String, &p);
        return CompleteOrFail(id, tcs, err, $"set '{name}'");
    }

    public Task SetPropertyAsync(string name, double value)
    {
        var (id, tcs) = NewRequest();
        using var n = new Utf8.NativeString(name);
        int err = LibMpv.mpv_set_property_async(Handle, id, n.Pointer, MpvFormat.Double, &value);
        return CompleteOrFail(id, tcs, err, $"set '{name}'");
    }

    public Task SetPropertyAsync(string name, long value)
    {
        var (id, tcs) = NewRequest();
        using var n = new Utf8.NativeString(name);
        int err = LibMpv.mpv_set_property_async(Handle, id, n.Pointer, MpvFormat.Int64, &value);
        return CompleteOrFail(id, tcs, err, $"set '{name}'");
    }

    public Task SetPropertyAsync(string name, bool value)
    {
        var (id, tcs) = NewRequest();
        int flag = value ? 1 : 0;
        using var n = new Utf8.NativeString(name);
        int err = LibMpv.mpv_set_property_async(Handle, id, n.Pointer, MpvFormat.Flag, &flag);
        return CompleteOrFail(id, tcs, err, $"set '{name}'");
    }

    public string? GetString(string name)
    {
        using var n = new Utf8.NativeString(name);
        byte* p = LibMpv.mpv_get_property_string(Handle, n.Pointer);
        if (p == null) return null;
        try { return Utf8.ToManaged(p); }
        finally { LibMpv.mpv_free(p); }
    }

    public long? GetInt64(string name)
    {
        long v;
        using var n = new Utf8.NativeString(name);
        return LibMpv.mpv_get_property(Handle, n.Pointer, MpvFormat.Int64, &v) >= 0 ? v : null;
    }

    public double? GetDouble(string name)
    {
        double v;
        using var n = new Utf8.NativeString(name);
        return LibMpv.mpv_get_property(Handle, n.Pointer, MpvFormat.Double, &v) >= 0 ? v : null;
    }

    public bool? GetFlag(string name)
    {
        int v;
        using var n = new Utf8.NativeString(name);
        return LibMpv.mpv_get_property(Handle, n.Pointer, MpvFormat.Flag, &v) >= 0 ? v != 0 : null;
    }

    /// <summary>Reads a property as a managed node tree (list/dictionary/primitive), or null if unavailable.</summary>
    public object? GetNode(string name)
    {
        MpvNode node;
        using var n = new Utf8.NativeString(name);
        if (LibMpv.mpv_get_property(Handle, n.Pointer, MpvFormat.Node, &node) < 0) return null;
        try { return NodeMarshal.ToManaged(&node); }
        finally { LibMpv.mpv_free_node_contents(&node); }
    }

    // ------------------------------------------------------------------ observation

    public enum ObserveFormat { String = 1, Flag = 3, Int64 = 4, Double = 5, Node = 6 }

    /// <summary>Starts observing a property; changes arrive via <see cref="PropertyChanged"/>.</summary>
    public ulong Observe(string name, ObserveFormat format)
    {
        ulong id = (ulong)Interlocked.Increment(ref _nextReplyId) | (1UL << 63);
        _observed[id] = name;
        using var n = new Utf8.NativeString(name);
        Check(LibMpv.mpv_observe_property(Handle, id, n.Pointer, (MpvFormat)format), $"observe '{name}'");
        return id;
    }

    public void Unobserve(ulong id)
    {
        if (_observed.TryRemove(id, out _)) LibMpv.mpv_unobserve_property(Handle, id);
    }

    // ------------------------------------------------------------------ commands

    /// <summary>Runs a command synchronously. Throws <see cref="MpvException"/> on failure.</summary>
    public void Command(params string[] args)
    {
        using var a = new Utf8.NativeStringArray(args);
        Check(LibMpv.mpv_command(Handle, a.Pointer), $"command '{args.FirstOrDefault()}'");
    }

    /// <summary>Runs a command synchronously; returns the mpv error code instead of throwing.</summary>
    public int TryCommand(params string[] args)
    {
        if (_disposed) return -3;
        using var a = new Utf8.NativeStringArray(args);
        return LibMpv.mpv_command(_handle, a.Pointer);
    }

    /// <summary>Runs a command synchronously and returns its result node.</summary>
    public object? CommandWithResult(params string[] args)
    {
        MpvNode result;
        using var a = new Utf8.NativeStringArray(args);
        Check(LibMpv.mpv_command_ret(Handle, a.Pointer, &result), $"command '{args.FirstOrDefault()}'");
        try { return NodeMarshal.ToManaged(&result); }
        finally { LibMpv.mpv_free_node_contents(&result); }
    }

    /// <summary>
    /// Runs a command and hands the raw result node to <paramref name="read"/> before it is freed.
    /// Lets callers copy large byte arrays (e.g. screenshot-raw) without an intermediate managed copy.
    /// </summary>
    internal T CommandWithRawResult<T>(Func<nint, T> read, params string[] args)
    {
        MpvNode result;
        using var a = new Utf8.NativeStringArray(args);
        Check(LibMpv.mpv_command_ret(Handle, a.Pointer, &result), $"command '{args.FirstOrDefault()}'");
        try { return read((nint)(&result)); }
        finally { LibMpv.mpv_free_node_contents(&result); }
    }

    /// <summary>Runs a command asynchronously (never blocks the caller); the task carries the result.</summary>
    public Task<object?> CommandAsync(params string[] args)
    {
        var (id, tcs) = NewRequest();
        using var a = new Utf8.NativeStringArray(args);
        int err = LibMpv.mpv_command_async(Handle, id, a.Pointer);
        if (err < 0)
        {
            _pending.TryRemove(id, out _);
            tcs.TrySetException(new MpvException(err, $"command '{args.FirstOrDefault()}'"));
        }
        return tcs.Task;
    }

    /// <summary>Runs a command given as a node (e.g. with named arguments) asynchronously.</summary>
    public Task<object?> CommandNodeAsync(IReadOnlyDictionary<string, object?> namedArgs)
    {
        var (id, tcs) = NewRequest();
        MpvNode node;
        NodeMarshal.FromManaged(namedArgs, &node);
        try
        {
            int err = LibMpv.mpv_command_node_async(Handle, id, &node);
            if (err < 0)
            {
                _pending.TryRemove(id, out _);
                tcs.TrySetException(new MpvException(err, "command"));
            }
        }
        finally { NodeMarshal.Free(&node); }
        return tcs.Task;
    }

    /// <summary>Fire-and-forget helper that swallows errors (logs them via the returned task only).</summary>
    public void Post(params string[] args) => _ = CommandAsync(args).ContinueWith(
        static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

    // ------------------------------------------------------------------ helpers

    public static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    private (ulong, TaskCompletionSource<object?>) NewRequest()
    {
        ulong id = (ulong)Interlocked.Increment(ref _nextReplyId);
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        return (id, tcs);
    }

    private Task CompleteOrFail(ulong id, TaskCompletionSource<object?> tcs, int err, string context)
    {
        if (err < 0)
        {
            _pending.TryRemove(id, out _);
            tcs.TrySetException(new MpvException(err, context));
        }
        return tcs.Task;
    }

    private static void Check(int err, string context)
    {
        if (err < 0) throw new MpvException(err, context);
    }

    // ------------------------------------------------------------------ event loop

    private void EventLoop()
    {
        nint h = _handle;
        while (true)
        {
            MpvEvent* ev = LibMpv.mpv_wait_event(h, -1);
            if (ev == null) continue;
            var id = ev->EventId;
            if (id == MpvEventId.None) { if (_disposed) break; continue; }
            try
            {
                Dispatch(ev);
            }
            catch (Exception ex)
            {
                // A subscriber failure must never kill the event loop.
                SafeLog(new MpvLogMessage("otear", MpvLogLevel.Error, $"event handler failed: {ex}"));
            }
            if (id == MpvEventId.Shutdown) break;
        }

        foreach (var (_, tcs) in _pending)
            tcs.TrySetException(new ObjectDisposedException(nameof(MpvClient)));
        _pending.Clear();
    }

    private void Dispatch(MpvEvent* ev)
    {
        switch (ev->EventId)
        {
            case MpvEventId.PropertyChange:
            {
                var prop = (MpvEventProperty*)ev->Data;
                string name = (_observed.TryGetValue(ev->ReplyUserdata, out var n) ? n : Utf8.ToManaged(prop->Name)) ?? string.Empty;
                object? value = prop->Format == MpvFormat.None ? null : NodeMarshal.FromPropertyData(prop->Format, prop->Data);
                PropertyChanged?.Invoke(new MpvPropertyChange(name, value));
                break;
            }
            case MpvEventId.CommandReply:
            {
                if (_pending.TryRemove(ev->ReplyUserdata, out var tcs))
                {
                    if (ev->Error < 0) tcs.TrySetException(new MpvException(ev->Error, "async command"));
                    else
                    {
                        var cmd = (MpvEventCommand*)ev->Data;
                        tcs.TrySetResult(cmd == null ? null : NodeMarshal.ToManaged(&cmd->Result));
                    }
                }
                break;
            }
            case MpvEventId.SetPropertyReply:
            case MpvEventId.GetPropertyReply:
            {
                if (_pending.TryRemove(ev->ReplyUserdata, out var tcs))
                {
                    if (ev->Error < 0) tcs.TrySetException(new MpvException(ev->Error, "async property"));
                    else tcs.TrySetResult(null);
                }
                break;
            }
            case MpvEventId.LogMessage:
            {
                var msg = (MpvEventLogMessage*)ev->Data;
                SafeLog(new MpvLogMessage(
                    Utf8.ToManaged(msg->Prefix) ?? string.Empty,
                    msg->LogLevel,
                    (Utf8.ToManaged(msg->Text) ?? string.Empty).TrimEnd('\n')));
                break;
            }
            case MpvEventId.StartFile:
            {
                var sf = (MpvEventStartFile*)ev->Data;
                StartFile?.Invoke(sf == null ? 0 : sf->PlaylistEntryId);
                break;
            }
            case MpvEventId.EndFile:
            {
                var ef = (MpvEventEndFile*)ev->Data;
                EndFile?.Invoke(new MpvEndFile(ef->Reason, ef->Error, ef->PlaylistEntryId));
                break;
            }
            case MpvEventId.ClientMessage:
            {
                var cm = (MpvEventClientMessage*)ev->Data;
                var args = new string[cm->NumArgs];
                for (int i = 0; i < cm->NumArgs; i++) args[i] = Utf8.ToManaged(cm->Args[i]) ?? string.Empty;
                ClientMessage?.Invoke(args);
                break;
            }
            default:
                EventRaised?.Invoke(new MpvSimpleEvent(ev->EventId));
                break;
        }
    }

    private void SafeLog(MpvLogMessage msg)
    {
        try { LogMessage?.Invoke(msg); } catch { /* never throw from logging */ }
    }

    // ------------------------------------------------------------------ shutdown

    /// <summary>Shuts the core down. Blocks until mpv has released its resources (bounded by a timeout).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        nint h = _handle;
        if (h == 0) return;

        using (var q = new Utf8.NativeStringArray(["quit"]))
            LibMpv.mpv_command(h, q.Pointer);
        if (_eventThread is { } t && t != Thread.CurrentThread)
        {
            if (!t.Join(TimeSpan.FromSeconds(5)))
            {
                LibMpv.mpv_wakeup(h);
                t.Join(TimeSpan.FromSeconds(1));
            }
        }
        LibMpv.mpv_terminate_destroy(h);
        _handle = 0;
    }
}
