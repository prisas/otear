using Microsoft.UI.Xaml.Media;
using Otear.Services;

namespace Otear;

/// <summary>Startup milestones and the optional auto-close used by tools/Measure-Startup.ps1.</summary>
public sealed partial class MainWindow
{
    private bool _startupWithFile;
    private IReadOnlyList<string> _startupItems = [];

    /// <summary>Records the first rendered frame after activation, then reports startup when complete.</summary>
    internal void WatchFirstFrame()
    {
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            StartupTrace.Mark("firstFrame");
            PrewarmSettingsWindow();
            // Language names for track menus are built from the OS culture list (tens of ms): do it off-thread.
            Task.Run(Otear.Mpv.MpvTrack.PrewarmLanguageNames);
            CheckStartupDone();
        }
        CompositionTarget.Rendering += OnRendering;
        _player.Ready.ContinueWith(_ => DispatcherQueue.TryEnqueue(CheckStartupDone), TaskScheduler.Default);
        _player.PlaybackRestarted += OnFirstPlayback;

        StartupTrace.StartupReported += () =>
        {
            if (Environment.GetEnvironmentVariable("OTEAR_AUTOCLOSE_MS") is { Length: > 0 } s && int.TryParse(s, out int ms))
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(ms);
                    // Same path as the user clicking the close button.
                    Otear.Interop.Win32.PostMessage(_hwnd, Otear.Interop.Win32.WM_CLOSE, 0, 0);
                });
        };
        // Never wait forever (e.g. a file that fails to open).
        _ = Task.Delay(20000).ContinueWith(_ => StartupTrace.Report(), TaskScheduler.Default);
    }

    private void OnFirstPlayback()
    {
        _player.PlaybackRestarted -= OnFirstPlayback;
        StartupTrace.Mark("playing");
        CheckStartupDone();
    }

    private void CheckStartupDone()
    {
        if (StartupTrace.Has("firstFrame") && StartupTrace.Has("mpvReady") && (!_startupWithFile || StartupTrace.Has("playing")))
            StartupTrace.Report();
    }
}
