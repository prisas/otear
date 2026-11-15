using System.Runtime.InteropServices;

namespace Otear.Mpv.Native;

/// <summary>Raw bindings for libmpv's client.h (client API 2.x).</summary>
internal static unsafe partial class LibMpv
{
    public const string Library = "libmpv-2.dll";

    // "unsigned long" is 32-bit on Windows (LLP64).
    [LibraryImport(Library)]
    public static partial uint mpv_client_api_version();

    [LibraryImport(Library)]
    public static partial byte* mpv_error_string(int error);

    [LibraryImport(Library)]
    public static partial void mpv_free(void* data);

    [LibraryImport(Library)]
    public static partial nint mpv_create();

    [LibraryImport(Library)]
    public static partial int mpv_initialize(nint ctx);

    [LibraryImport(Library)]
    public static partial void mpv_destroy(nint ctx);

    [LibraryImport(Library)]
    public static partial void mpv_terminate_destroy(nint ctx);

    [LibraryImport(Library)]
    public static partial nint mpv_create_client(nint ctx, byte* name);

    [LibraryImport(Library)]
    public static partial void mpv_free_node_contents(MpvNode* node);

    [LibraryImport(Library)]
    public static partial int mpv_set_option(nint ctx, byte* name, MpvFormat format, void* data);

    [LibraryImport(Library)]
    public static partial int mpv_set_option_string(nint ctx, byte* name, byte* data);

    [LibraryImport(Library)]
    public static partial int mpv_command(nint ctx, byte** args);

    [LibraryImport(Library)]
    public static partial int mpv_command_node(nint ctx, MpvNode* args, MpvNode* result);

    [LibraryImport(Library)]
    public static partial int mpv_command_ret(nint ctx, byte** args, MpvNode* result);

    [LibraryImport(Library)]
    public static partial int mpv_command_async(nint ctx, ulong replyUserdata, byte** args);

    [LibraryImport(Library)]
    public static partial int mpv_command_node_async(nint ctx, ulong replyUserdata, MpvNode* args);

    [LibraryImport(Library)]
    public static partial void mpv_abort_async_command(nint ctx, ulong replyUserdata);

    [LibraryImport(Library)]
    public static partial int mpv_set_property(nint ctx, byte* name, MpvFormat format, void* data);

    [LibraryImport(Library)]
    public static partial int mpv_set_property_string(nint ctx, byte* name, byte* data);

    [LibraryImport(Library)]
    public static partial int mpv_set_property_async(nint ctx, ulong replyUserdata, byte* name, MpvFormat format, void* data);

    [LibraryImport(Library)]
    public static partial int mpv_get_property(nint ctx, byte* name, MpvFormat format, void* data);

    [LibraryImport(Library)]
    public static partial byte* mpv_get_property_string(nint ctx, byte* name);

    [LibraryImport(Library)]
    public static partial int mpv_observe_property(nint ctx, ulong replyUserdata, byte* name, MpvFormat format);

    [LibraryImport(Library)]
    public static partial int mpv_unobserve_property(nint ctx, ulong registeredReplyUserdata);

    [LibraryImport(Library)]
    public static partial byte* mpv_event_name(MpvEventId eventId);

    [LibraryImport(Library)]
    public static partial int mpv_request_event(nint ctx, MpvEventId eventId, int enable);

    [LibraryImport(Library)]
    public static partial int mpv_request_log_messages(nint ctx, byte* minLevel);

    [LibraryImport(Library)]
    public static partial MpvEvent* mpv_wait_event(nint ctx, double timeout);

    [LibraryImport(Library)]
    public static partial void mpv_wakeup(nint ctx);
}

internal enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
    NodeArray = 7,
    NodeMap = 8,
    ByteArray = 9,
}

public enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    GetPropertyReply = 3,
    SetPropertyReply = 4,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    Idle = 11,
    Tick = 14,
    ClientMessage = 16,
    VideoReconfig = 17,
    AudioReconfig = 18,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22,
    QueueOverflow = 24,
    Hook = 25,
}

public enum MpvLogLevel
{
    None = 0,
    Fatal = 10,
    Error = 20,
    Warn = 30,
    Info = 40,
    Verbose = 50,
    Debug = 60,
    Trace = 70,
}

public enum MpvEndFileReason
{
    Eof = 0,
    Stop = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5,
}

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct MpvNode
{
    [FieldOffset(0)] public byte* String;
    [FieldOffset(0)] public int Flag;
    [FieldOffset(0)] public long Int64;
    [FieldOffset(0)] public double Double;
    [FieldOffset(0)] public MpvNodeList* List;
    [FieldOffset(0)] public MpvByteArray* ByteArray;
    [FieldOffset(8)] public MpvFormat Format;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvNodeList
{
    public int Num;
    public MpvNode* Values;
    public byte** Keys;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvByteArray
{
    public void* Data;
    public nuint Size;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvEvent
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserdata;
    public void* Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvEventProperty
{
    public byte* Name;
    public MpvFormat Format;
    public void* Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvEventLogMessage
{
    public byte* Prefix;
    public byte* Level;
    public byte* Text;
    public MpvLogLevel LogLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventStartFile
{
    public long PlaylistEntryId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
    public MpvEndFileReason Reason;
    public int Error;
    public long PlaylistEntryId;
    public long PlaylistInsertId;
    public int PlaylistInsertNumEntries;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MpvEventClientMessage
{
    public int NumArgs;
    public byte** Args;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventCommand
{
    public MpvNode Result;
}
