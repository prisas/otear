namespace Otear.Services;

/// <summary>Per-user locations used by the app.</summary>
internal static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Otear");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string RecentFile => Path.Combine(Root, "recent.json");
    public static string WatchLater => Path.Combine(Root, "watch_later");
    public static string ShaderCache => Path.Combine(Root, "cache", "shaders");
    public static string Logs => Path.Combine(Root, "logs");
    public static string AppLog => Path.Combine(Logs, "otear.log");
    public static string MpvLog => Path.Combine(Logs, "mpv.log");

    /// <summary>Optional mpv config dir (mpv.conf, input.conf, scripts) used when the user enables it.</summary>
    public static string MpvConfig { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Otear", "mpv");

    public static string Screenshots { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Otear");

    public static string AppDirectory => AppContext.BaseDirectory;
    public static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    public static void EnsureCreated()
    {
        foreach (var dir in new[] { Root, WatchLater, ShaderCache, Logs })
        {
            try { Directory.CreateDirectory(dir); } catch { /* best effort */ }
        }
    }
}
