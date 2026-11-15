using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Otear.Interop;

namespace Otear;

public static class Program
{
    private static string[] s_args = [];

    [STAThread]
    private static int Main(string[] args)
    {
        Otear.Services.StartupTrace.Mark("main");
        s_args = args;
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single instance: a second launch hands its files to the running window and exits.
        if (!args.Contains("--new-window"))
        {
            var main = AppInstance.FindOrRegisterForKey("Otear.Main");
            if (!main.IsCurrent)
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
                try
                {
                    Task.Run(async () => await main.RedirectActivationToAsync(activation)).Wait(TimeSpan.FromSeconds(10));
                }
                catch (Exception)
                {
                    // If redirection fails we simply exit; the running instance is still there.
                }
                return 0;
            }
        }

        Otear.Services.StartupTrace.Mark("instanceCheck");
        // The window's title bar renders with Direct3D's software rasteriser (WARP). Initialising it on demand
        // collides with the compositor's GPU driver start-up on the critical path; doing it now, in parallel,
        // brings the first frame forward by ~30 ms (tools/Measure-Startup.ps1).
        GpuWarmup.StartWarp();
        Application.Start(static _ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(s_args);
        });
        Otear.Services.Log.Info("ui loop ended");
        return 0;
    }
}
