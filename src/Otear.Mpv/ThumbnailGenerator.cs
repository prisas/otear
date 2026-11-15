using System.Collections.Concurrent;
using System.Globalization;
using Otear.Mpv.Native;

namespace Otear.Mpv;

/// <summary>
/// Generates seek-bar preview frames for one file with a second, headless mpv core
/// (vo=null, keyframe seeks, downscaled by an FFmpeg filter, read back with screenshot-raw).
/// Runs on its own low-priority thread; frames near the user's hover position are made first.
/// </summary>
public sealed class ThumbnailGenerator : IDisposable
{
    /// <summary>A BGRA8 (opaque) frame.</summary>
    public sealed record Frame(int Width, int Height, byte[] Bgra);

    private readonly string _path;
    private readonly double _duration;
    private readonly int _width;
    private readonly bool _hwdec;
    private readonly Frame?[] _frames;
    private readonly bool[] _attempted;
    private Thread? _thread;
    private volatile bool _stop;
    private volatile int _focus = -1;
    private long _focusTicks;

    public int Count { get; }

    /// <summary>Wait this long before decoding (lets playback start without competing), unless the user hovers.</summary>
    public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Pause between frames while nobody is looking at previews, to keep background load low.</summary>
    public TimeSpan IdleGap { get; init; } = TimeSpan.FromMilliseconds(40);

    private bool Focused => _focus >= 0 && Environment.TickCount64 - Interlocked.Read(ref _focusTicks) < 3000;
    public int Generated { get; private set; }
    public bool IsComplete { get; private set; }

    /// <summary>Raised on the generator thread when a frame is ready.</summary>
    public event Action<int>? FrameReady;

    public ThumbnailGenerator(string path, double duration, int width = 240, bool hardwareDecoding = true)
    {
        _path = path;
        _duration = duration;
        _width = width;
        _hwdec = hardwareDecoding;
        // One frame every ~5 s, between 10 and 200 frames.
        Count = (int)Math.Clamp(Math.Round(duration / 5), 10, 200);
        _frames = new Frame?[Count];
        _attempted = new bool[Count];
    }

    public int IndexFor(double time) => (int)Math.Clamp(Math.Floor(time / _duration * Count), 0, Count - 1);
    public double TimeFor(int index) => Math.Min(_duration - 0.1, (index + 0.5) * _duration / Count);

    /// <summary>The frame for a time, or the closest one generated so far (null if none nearby).</summary>
    public Frame? Get(double time)
    {
        int idx = IndexFor(time);
        for (int d = 0; d < 4; d++)
        {
            if (idx - d >= 0 && _frames[idx - d] is { } a) return a;
            if (idx + d < Count && _frames[idx + d] is { } b) return b;
        }
        return null;
    }

    /// <summary>Asks the generator to work around this time next.</summary>
    public void Focus(double time)
    {
        _focus = IndexFor(time);
        Interlocked.Exchange(ref _focusTicks, Environment.TickCount64);
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "thumbnails", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private IEnumerable<KeyValuePair<string, string>> Options() =>
    [
        new("vo", "null"),
        new("ao", "null"),
        new("aid", "no"),
        new("sid", "no"),
        new("pause", "yes"),
        new("idle", "yes"),
        new("keep-open", "always"),
        new("config", "no"),
        new("load-scripts", "no"),
        new("osc", "no"),
        new("ytdl", "no"),
        new("terminal", "no"),
        new("input-default-bindings", "no"),
        new("resume-playback", "no"),
        new("sub-auto", "no"),
        new("audio-file-auto", "no"),
        new("cache", "no"),
        new("hr-seek", "no"),
        new("hwdec", _hwdec ? "auto-copy-safe" : "no"),
        new("vd-lavc-skiploopfilter", "all"),
        new("vd-lavc-threads", "2"),
        new("vf", string.Create(CultureInfo.InvariantCulture, $"scale=w={_width}:h=-2:flags=fast_bilinear")),
    ];

    private int NextIndex()
    {
        int focus = _focus;
        if (Focused)
        {
            for (int d = 0; d < Count; d++)
            {
                if (focus + d < Count && !_attempted[focus + d]) return focus + d;
                if (focus - d >= 0 && !_attempted[focus - d]) return focus - d;
            }
            return -1;
        }
        return Array.IndexOf(_attempted, false);
    }

    private void Run()
    {
        MpvClient? mpv = null;
        try
        {
            long startAt = Environment.TickCount64 + (long)StartDelay.TotalMilliseconds;
            while (!_stop && !Focused && Environment.TickCount64 < startAt) Thread.Sleep(50);
            if (_stop) return;
            mpv = MpvClient.Create(Options(), logLevel: null);
            var events = new BlockingCollection<MpvEventId>();
            mpv.EventRaised += e => events.Add(e.Id);
            mpv.EndFile += _ => events.Add(MpvEventId.EndFile);

            mpv.Command("loadfile", _path);
            if (!WaitFor(events, MpvEventId.FileLoaded, 15_000)) return;

            while (!_stop)
            {
                int i = NextIndex();
                if (i < 0) break;
                _attempted[i] = true;
                while (events.TryTake(out _)) { }
                mpv.Command("seek", MpvClient.Format(TimeFor(i)), "absolute+keyframes");
                if (!WaitFor(events, MpvEventId.PlaybackRestart, 5_000)) continue;
                if (_stop) break;
                Frame? frame;
                try { frame = mpv.CommandWithRawResult(ReadFrame, "screenshot-raw", "video"); }
                catch (MpvException) { continue; }
                if (frame is null) continue;
                _frames[i] = frame;
                Generated++;
                FrameReady?.Invoke(i);
                if (!Focused && IdleGap > TimeSpan.Zero) Thread.Sleep(IdleGap);
            }
            IsComplete = !_stop;
        }
        catch (Exception)
        {
            // Thumbnails are best-effort: unsupported files just have none.
        }
        finally
        {
            mpv?.Dispose();
        }
    }

    private bool WaitFor(BlockingCollection<MpvEventId> events, MpvEventId id, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!_stop)
        {
            long left = deadline - Environment.TickCount64;
            if (left <= 0 || !events.TryTake(out var ev, (int)Math.Min(left, 250))) { if (left <= 0) return false; continue; }
            if (ev == id) return true;
            if (ev is MpvEventId.EndFile or MpvEventId.Shutdown) return false;
        }
        return false;
    }

    /// <summary>Copies a screenshot-raw result (map with w, h, stride, format, data) into opaque BGRA.</summary>
    private static unsafe Frame? ReadFrame(nint nodePtr)
    {
        var node = (MpvNode*)nodePtr;
        if (node->Format != MpvFormat.NodeMap || node->List == null) return null;
        long w = 0, h = 0, stride = 0;
        string? format = null;
        MpvByteArray* data = null;
        var list = node->List;
        for (int k = 0; k < list->Num; k++)
        {
            string key = Utf8.ToManaged(list->Keys[k]) ?? string.Empty;
            var v = &list->Values[k];
            switch (key)
            {
                case "w": w = v->Int64; break;
                case "h": h = v->Int64; break;
                case "stride": stride = v->Int64; break;
                case "format": format = Utf8.ToManaged(v->String); break;
                case "data" when v->Format == MpvFormat.ByteArray: data = v->ByteArray; break;
            }
        }
        if (w <= 0 || h <= 0 || stride < w * 4 || data == null || data->Data == null) return null;
        if (format is not ("bgr0" or "bgra")) return null;
        if ((long)data->Size < stride * (h - 1) + w * 4) return null;

        var pixels = new byte[w * h * 4];
        fixed (byte* dst = pixels)
        {
            byte* src = (byte*)data->Data;
            for (long y = 0; y < h; y++)
            {
                var row = (uint*)(src + y * stride);
                var outRow = (uint*)(dst + y * w * 4);
                for (long x = 0; x < w; x++) outRow[x] = row[x] | 0xFF000000u; // force opaque alpha
            }
        }
        return new Frame((int)w, (int)h, pixels);
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread is { } t && t != Thread.CurrentThread) t.Join(TimeSpan.FromSeconds(3));
    }
}
