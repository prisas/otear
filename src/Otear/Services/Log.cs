using System.Text;

namespace Otear.Services;

/// <summary>Small thread-safe file logger (logs/otear.log, rotated at 2 MB).</summary>
internal static class Log
{
    private static readonly Lock s_lock = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    public static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        lock (s_lock)
        {
            try
            {
                var path = AppPaths.AppLog;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = Path.ChangeExtension(path, ".old.log");
                    File.Copy(path, old, overwrite: true);
                    File.Delete(path);
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
