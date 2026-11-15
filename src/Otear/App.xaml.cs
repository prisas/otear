using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Otear.Interop;
using Otear.Services;
using Windows.ApplicationModel.Activation;

namespace Otear;

public partial class App : Application
{
    private readonly string[] _args;
    private MainWindow? _window;

    public App(string[] args)
    {
        _args = args;
        StartupTrace.Mark("app");
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Log and keep running: a failed UI callback must not take playback down.
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Info("process exit");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs e)
    {
        StartupTrace.Mark("launched");
        // The window first: creating it starts the compositor's GPU device initialisation, the longest part of
        // startup, which then runs while the rest is set up.
        _window = new MainWindow();
        AppPaths.EnsureCreated();
        Log.Info($"Otear starting ({Environment.ProcessPath})");
        var settings = new SettingsService();
        StartupTrace.Mark("settings");
        // mpv loads in the background, in parallel with the rest of the window setup.
        var player = new Otear.Player.PlayerController(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), settings.Settings);
        player.Start();
        _window.Initialize(settings, player, MediaFiles.Expand(ParseItems(_args)));
        AppInstance.GetCurrent().Activated += OnRedirectedActivation;
        _window.Show();
    }

    /// <summary>Files passed to a second Otear process (Explorer "Open with") arrive here.</summary>
    private void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        string[] items = [];
        if (e.Kind == ExtendedActivationKind.Launch && e.Data is ILaunchActivatedEventArgs launch)
        {
            var argv = Win32.SplitCommandLine(launch.Arguments);
            // Unpackaged launches include the executable path as the first token.
            if (argv.Length > 0 && argv[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) argv = argv[1..];
            items = ParseItems(argv);
        }
        _window?.DispatcherQueue.TryEnqueue(() => _window.HandleExternalActivation(items));
    }

    internal static string[] ParseItems(IEnumerable<string> args) =>
        args.Where(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
}
