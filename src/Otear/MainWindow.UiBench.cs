using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Otear.Services;
using Otear.Views;

namespace Otear;

/// <summary>
/// UI responsiveness benchmark (OTEAR_BENCH_UI=&lt;output file&gt;, with a file on the command line): times how long
/// panels, menus and the settings window take to appear, the work done while a slider is dragged, and the
/// UI-thread latency during playback. Appends one JSON line and closes the app.
/// </summary>
public sealed partial class MainWindow
{
    private async Task RunUiBenchmarkAsync(string output)
    {
        var results = new List<(string, double)>();
        try
        {
            await _player.Ready;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!_player.IsLoaded && DateTime.UtcNow < deadline) await Task.Delay(100);
            _player.SetRaw("loop-file", "inf");
            await Task.Delay(2500);
            Player.NotifyActivity();

            async Task Time(string name, Func<Task> action)
            {
                Player.NotifyActivity();
                await NextFramesAsync(2);
                var sw = Stopwatch.StartNew();
                await action();
                results.Add((name, sw.Elapsed.TotalMilliseconds));
                await Task.Delay(300);
            }

            await Time("quickSettingsOpenMs", async () => { Player.ToggleQuickSettings(); await NextFramesAsync(2); });
            await Time("quickTabAudioMs", async () => { Player.SelectQuickTab(1); await NextFramesAsync(2); });
            await Time("quickTabSubtitlesMs", async () => { Player.SelectQuickTab(2); await NextFramesAsync(2); });

            // Simulated slider drag: 30 subtitle-delay changes at ~60 Hz while the panel is open.
            var (r0, t0) = (PerfCounters.QuickRefreshes, PerfCounters.QuickRefreshTicks);
            var lat = LatencyProbe.Start(DispatcherQueue);
            for (int i = 1; i <= 30; i++) { _player.SetSubtitleDelay(i * 0.05); await Task.Delay(16); }
            await Task.Delay(400);
            var dragLatency = await lat.StopAsync();
            results.Add(("dragPanelRefreshes", PerfCounters.QuickRefreshes - r0));
            results.Add(("dragPanelRefreshMs", Stopwatch.GetElapsedTime(0, PerfCounters.QuickRefreshTicks - t0).TotalMilliseconds));
            results.Add(("dragUiLatencyP95Ms", dragLatency.P95));
            results.Add(("dragUiLatencyMaxMs", dragLatency.Max));
            _player.SetSubtitleDelay(0);

            await Time("playlistOpenMs", async () => { Player.TogglePlaylist(); await NextFramesAsync(2); });
            await Time("panelCloseMs", async () => { Player.CloseSidePanel(); await NextFramesAsync(2); });

            await Time("contextMenuMs", async () =>
            {
                var menu = ContextMenu.Build(_player, this, Player);
                var opened = new TaskCompletionSource();
                menu.Opened += (_, _) => opened.TrySetResult();
                menu.ShowAt(Video, new FlyoutShowOptions { Position = new Windows.Foundation.Point(200, 200) });
                await opened.Task;
                await NextFramesAsync(1);
                menu.Hide();
            });

            await Time("audioMenuMs", async () =>
            {
                var flyout = Player.ControlBar.OpenAudioMenu();
                var opened = new TaskCompletionSource();
                flyout.Opened += (_, _) => opened.TrySetResult();
                await opened.Task;
                await NextFramesAsync(1);
                flyout.Hide();
            });

            for (int i = 1; i <= 2; i++)
            {
                await Time($"settingsOpen{i}Ms", async () =>
                {
                    ShowSettings();
                    await NextFramesAsync(3);
                });
                if (_settingsWindow is not null) Otear.Interop.Win32.PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(_settingsWindow), Otear.Interop.Win32.WM_CLOSE, 0, 0);
                await Task.Delay(500);
            }

            // UI-thread latency while playing with the controls visible.
            var probe = LatencyProbe.Start(DispatcherQueue);
            var end = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < end) { Player.NotifyActivity(); await Task.Delay(200); }
            var playLatency = await probe.StopAsync();
            results.Add(("playUiLatencyP50Ms", playLatency.P50));
            results.Add(("playUiLatencyP95Ms", playLatency.P95));
            results.Add(("playUiLatencyMaxMs", playLatency.Max));

            var sb = new StringBuilder("{");
            sb.Append(CultureInfo.InvariantCulture, $"\"label\":\"{Environment.GetEnvironmentVariable("OTEAR_BENCH_LABEL")}\"");
            foreach (var (k, v) in results) sb.Append(CultureInfo.InvariantCulture, $",\"{k}\":{v:0.#}");
            sb.Append('}');
            File.AppendAllText(output, sb + Environment.NewLine);
            Log.Info("ui benchmark: " + sb);
        }
        catch (Exception ex)
        {
            Log.Error("ui benchmark failed", ex);
        }
        Close();
    }

    internal static Task NextFramesAsync(int frames)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int seen = 0;
        void OnRendering(object? sender, object e)
        {
            if (++seen < frames) return;
            CompositionTarget.Rendering -= OnRendering;
            tcs.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        return tcs.Task;
    }

    /// <summary>Measures how long UI-thread work items wait before running (every 25 ms).</summary>
    private sealed class LatencyProbe
    {
        private readonly List<double> _samples = [];
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public static LatencyProbe Start(DispatcherQueue dq)
        {
            var p = new LatencyProbe();
            p._loop = Task.Run(async () =>
            {
                while (!p._cts.IsCancellationRequested)
                {
                    long t = Stopwatch.GetTimestamp();
                    var done = new TaskCompletionSource();
                    dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                    {
                        lock (p._samples) p._samples.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
                        done.TrySetResult();
                    });
                    await Task.WhenAny(done.Task, Task.Delay(2000));
                    await Task.Delay(25);
                }
            });
            return p;
        }

        public async Task<(double P50, double P95, double Max)> StopAsync()
        {
            _cts.Cancel();
            if (_loop is not null) await _loop;
            double[] s;
            lock (_samples) s = _samples.OrderBy(x => x).ToArray();
            if (s.Length == 0) return (0, 0, 0);
            return (s[s.Length / 2], s[(int)(s.Length * 0.95)], s[^1]);
        }
    }
}
