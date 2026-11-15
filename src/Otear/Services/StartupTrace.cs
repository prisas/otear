using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Otear.Services;

/// <summary>
/// Startup / shutdown milestones (ms since the process started). One summary line is written to the log,
/// so startup and shutdown cost can be tracked over time (tools/Measure-Startup.ps1 reads it).
/// </summary>
internal static class StartupTrace
{
    private static readonly long s_t0 = Stopwatch.GetTimestamp();
    private static readonly double s_preMainMs = ComputePreMain();
    private static readonly List<(string Name, double Ms)> s_marks = [];
    private static readonly Lock s_lock = new();
    private static bool s_reported;

    /// <summary>Time spent by the OS/runtime before Main ran.</summary>
    private static double ComputePreMain()
    {
        try { return Math.Max(0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds); }
        catch { return 0; }
    }

    public static double Now => s_preMainMs + Stopwatch.GetElapsedTime(s_t0).TotalMilliseconds;

    public static void Mark(string name)
    {
        lock (s_lock)
        {
            if (!s_marks.Exists(m => m.Name == name)) s_marks.Add((name, Now));
        }
    }

    public static bool Has(string name)
    {
        lock (s_lock) return s_marks.Exists(m => m.Name == name);
    }

    /// <summary>Writes the startup line once.</summary>
    public static void Report()
    {
        string line;
        lock (s_lock)
        {
            if (s_reported) return;
            s_reported = true;
            var sb = new StringBuilder("startup:");
            sb.Append(CultureInfo.InvariantCulture, $" preMain={s_preMainMs:0}");
            foreach (var (name, ms) in s_marks) sb.Append(CultureInfo.InvariantCulture, $" {name}={ms:0}");
            line = sb.ToString();
        }
        Log.Info(line);
        StartupReported?.Invoke();
    }

    public static event Action? StartupReported;
}
