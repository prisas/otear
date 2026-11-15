using System.Diagnostics;
using System.Globalization;
using System.Text;
using Otear.Services;

namespace Otear;

/// <summary>
/// Playback benchmark, enabled by environment variables (inert otherwise):
///   OTEAR_BENCH_SECONDS   measured playback time (after a 4 s warm-up)
///   OTEAR_BENCH_OUT       file to append one result line to
///   OTEAR_BENCH_CONTROLS  "visible" keeps the control bar shown; default lets it auto-hide
///   OTEAR_BENCH_FULLSCREEN "1" plays full screen
/// The file to play is passed on the command line as usual. The app exits when done.
/// </summary>
public sealed partial class MainWindow
{
    private async Task RunBenchmarkAsync(double seconds, string output)
    {
        bool keepVisible = Environment.GetEnvironmentVariable("OTEAR_BENCH_CONTROLS") == "visible";
        bool fullscreen = Environment.GetEnvironmentVariable("OTEAR_BENCH_FULLSCREEN") == "1";
        try
        {
            await _player.Ready;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!_player.IsLoaded && DateTime.UtcNow < deadline) await Task.Delay(100);
            _player.SetRaw("loop-file", "inf");
            if (fullscreen && !IsFullscreen) ToggleFullscreen();

            async Task Wait(double s)
            {
                var end = DateTime.UtcNow.AddSeconds(s);
                while (DateTime.UtcNow < end)
                {
                    if (keepVisible) Player.NotifyActivity();
                    await Task.Delay(250);
                }
            }

            await Wait(4);
            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            var cpu0 = proc.TotalProcessorTime;
            var ui0 = PerfCounters.Snapshot();
            var mpv0 = ReadFrameCounters();
            var sw = Stopwatch.StartNew();

            await Wait(seconds);

            proc.Refresh();
            double wall = sw.Elapsed.TotalSeconds;
            double cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            var ui1 = PerfCounters.Snapshot();
            var mpv1 = ReadFrameCounters();

            var sb = new StringBuilder();
            string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            sb.Append($"{{\"label\":\"{Environment.GetEnvironmentVariable("OTEAR_BENCH_LABEL")}\",");
            sb.Append($"\"file\":\"{Path.GetFileName(_player.Path)}\",\"controls\":\"{(keepVisible ? "visible" : "auto")}\",\"fullscreen\":{(fullscreen ? "true" : "false")},");
            sb.Append($"\"seconds\":{F(wall)},\"cpuPercentOfOneCore\":{F(cpuMs / wall / 10)},");
            sb.Append($"\"uiFlushesPerSec\":{F((ui1.Flushes - ui0.Flushes) / wall)},\"uiAppliesPerSec\":{F((ui1.Applies - ui0.Applies) / wall)},");
            sb.Append($"\"oscUpdatesPerSec\":{F((ui1.OscTimeUpdates - ui0.OscTimeUpdates) / wall)},\"seekBarRedrawsPerSec\":{F((ui1.SeekBarRedraws - ui0.SeekBarRedraws) / wall)},");
            foreach (var (name, v0) in mpv0)
                sb.Append($"\"{name}\":{F(mpv1.TryGetValue(name, out var v1) ? v1 - v0 : double.NaN)},");
            sb.Append($"\"estimatedVfFps\":{F(double.TryParse(_player.GetRaw("estimated-vf-fps"), NumberStyles.Float, CultureInfo.InvariantCulture, out var vf) ? vf : 0)},");
            sb.Append($"\"workingSetMB\":{F(proc.WorkingSet64 / 1048576.0)},\"hwdec\":\"{_player.HwdecCurrent}\"}}");
            File.AppendAllText(output, sb + Environment.NewLine);
            Log.Info("benchmark: " + sb);
        }
        catch (Exception ex)
        {
            Log.Error("benchmark failed", ex);
        }
        Close();
    }

    private Dictionary<string, double> ReadFrameCounters()
    {
        var result = new Dictionary<string, double>();
        foreach (var name in new[] { "frame-drop-count", "decoder-frame-drop-count", "vo-delayed-frame-count", "mistimed-frame-count" })
        {
            if (double.TryParse(_player.GetRaw(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                result[name] = v;
        }
        return result;
    }
}
