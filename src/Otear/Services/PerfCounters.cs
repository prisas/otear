namespace Otear.Services;

/// <summary>Cheap counters of UI-thread work during playback (read by the benchmark mode).</summary>
internal static class PerfCounters
{
    /// <summary>Coalesced property batches delivered from mpv's event thread to the UI thread.</summary>
    public static long Flushes;
    /// <summary>Individual property changes applied on the UI thread.</summary>
    public static long Applies;
    /// <summary>Times the control bar re-rendered its time/seek state.</summary>
    public static long OscTimeUpdates;
    /// <summary>Seek bar redraws of buffered ranges / chapter ticks (element churn).</summary>
    public static long SeekBarRedraws;
    /// <summary>Quick-settings panel refreshes and the Stopwatch ticks spent in them.</summary>
    public static long QuickRefreshes, QuickRefreshTicks;
    /// <summary>Playlist panel refreshes and the Stopwatch ticks spent in them.</summary>
    public static long PlaylistRefreshes, PlaylistRefreshTicks;

    public static (long Flushes, long Applies, long OscTimeUpdates, long SeekBarRedraws) Snapshot() =>
        (Interlocked.Read(ref Flushes), Interlocked.Read(ref Applies), Interlocked.Read(ref OscTimeUpdates), Interlocked.Read(ref SeekBarRedraws));
}
