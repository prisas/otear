using Otear.Interop;

namespace Otear.Services;

/// <summary>Known media extensions and folder expansion for drag-and-drop / "Open folder".</summary>
internal static class MediaFiles
{
    public static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".mov", ".webm", ".avi", ".wmv", ".asf", ".flv", ".ts", ".m2ts", ".mts", ".m2t",
        ".mpg", ".mpeg", ".m2v", ".vob", ".ogv", ".3gp", ".3g2", ".mxf", ".rm", ".rmvb", ".divx", ".y4m", ".hevc", ".h264", ".av1", ".ivf",
    };

    public static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wav", ".wma", ".ape", ".alac", ".aiff", ".aif", ".mka", ".dts", ".ac3", ".eac3", ".wv", ".tta", ".mpc",
    };

    public static readonly HashSet<string> Playlist = new(StringComparer.OrdinalIgnoreCase) { ".m3u", ".m3u8", ".pls", ".cue" };

    public static readonly HashSet<string> Subtitle = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".smi", ".lrc", ".ttml", ".dfxp",
    };

    public static bool IsSubtitle(string path) => Subtitle.Contains(Path.GetExtension(path));

    public static bool IsPlayable(string path)
    {
        string ext = Path.GetExtension(path);
        return Video.Contains(ext) || Audio.Contains(ext) || Playlist.Contains(ext);
    }

    /// <summary>Replaces folders by the media files they contain (naturally sorted), keeping files and URLs as given.</summary>
    public static List<string> Expand(IEnumerable<string> items)
    {
        var result = new List<string>();
        foreach (var item in items)
        {
            if (Directory.Exists(item))
            {
                try
                {
                    var files = Directory.EnumerateFiles(item, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                        .Where(f => Video.Contains(Path.GetExtension(f)) || Audio.Contains(Path.GetExtension(f)))
                        .OrderBy(f => Path.GetDirectoryName(f), Win32.NaturalComparer.Instance)
                        .ThenBy(f => Path.GetFileName(f), Win32.NaturalComparer.Instance);
                    result.AddRange(files);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read folder {item}: {ex.Message}");
                }
            }
            else
            {
                result.Add(item);
            }
        }
        return result;
    }
}
