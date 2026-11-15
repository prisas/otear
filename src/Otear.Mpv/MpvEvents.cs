using Otear.Mpv.Native;

namespace Otear.Mpv;

/// <summary>A property value changed. <see cref="Value"/> is null when the property is unavailable.</summary>
public readonly record struct MpvPropertyChange(string Name, object? Value);

public readonly record struct MpvLogMessage(string Prefix, MpvLogLevel Level, string Text);

public readonly record struct MpvEndFile(MpvEndFileReason Reason, int Error, long PlaylistEntryId)
{
    public bool IsError => Reason == MpvEndFileReason.Error;
    public string ErrorMessage => Error == 0 ? string.Empty : MpvException.Describe(Error);
}

/// <summary>Generic event without payload (FileLoaded, VideoReconfig, PlaybackRestart, Seek, Idle, Shutdown...).</summary>
public readonly record struct MpvSimpleEvent(MpvEventId Id);
