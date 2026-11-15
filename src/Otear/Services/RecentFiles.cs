using System.Collections.ObjectModel;
using System.Text.Json;

namespace Otear.Services;

public sealed class RecentEntry
{
    public string Path { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime LastOpened { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
}

/// <summary>One row on the welcome screen.</summary>
public sealed partial class RecentItem
{
    public RecentItem(RecentEntry entry) => Entry = entry;

    public RecentEntry Entry { get; }
    public string Path => Entry.Path;

    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Entry.Title)) return Entry.Title!;
            try { return System.IO.Path.GetFileName(Entry.Path) is { Length: > 0 } n ? n : Entry.Path; }
            catch { return Entry.Path; }
        }
    }

    public string Detail
    {
        get
        {
            string where;
            try { where = Uri.TryCreate(Entry.Path, UriKind.Absolute, out var u) && !u.IsFile ? u.Host : System.IO.Path.GetDirectoryName(Entry.Path) ?? string.Empty; }
            catch { where = string.Empty; }
            string progress = HasProgress
                ? $" · {TimeFormat.Format(Entry.Position, Entry.Duration)} / {TimeFormat.Format(Entry.Duration, Entry.Duration)}"
                : string.Empty;
            return where + progress;
        }
    }

    /// <summary>Partly watched (same rule as resume: past 10 s and not within the last 10 s).</summary>
    private bool HasProgress => Entry.Duration > 0 && Entry.Position > 10 && Entry.Position < Entry.Duration - 10;
    public double Progress => HasProgress ? Math.Clamp(Entry.Position / Entry.Duration, 0, 1) * 100 : 0;
    public Microsoft.UI.Xaml.Visibility ProgressVisibility => HasProgress ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

/// <summary>Most-recently-opened media, persisted to recent.json.</summary>
internal sealed class RecentFilesService
{
    private const int MaxEntries = 20;
    private readonly List<RecentEntry> _entries;
    public ObservableCollection<RecentItem> Items { get; } = new();

    public RecentFilesService()
    {
        _entries = Load();
        Rebuild();
    }

    public void Touch(string path, string? title, double position, double duration)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var e = _entries.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        if (e is null)
        {
            e = new RecentEntry { Path = path };
            _entries.Insert(0, e);
        }
        else
        {
            _entries.Remove(e);
            _entries.Insert(0, e);
        }
        e.Title = string.IsNullOrWhiteSpace(title) || title == System.IO.Path.GetFileName(path) ? null : title;
        e.LastOpened = DateTime.Now;
        if (duration > 0) { e.Position = position; e.Duration = duration; }
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        Save();
        Rebuild();
    }

    public void UpdatePosition(string path, double position, double duration)
    {
        var e = _entries.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        if (e is null || duration <= 0) return;
        e.Position = position;
        e.Duration = duration;
        Save();
        Rebuild();
    }

    public void Remove(string path)
    {
        _entries.RemoveAll(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        Save();
        Rebuild();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
        Rebuild();
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var e in _entries) Items.Add(new RecentItem(e));
    }

    private static List<RecentEntry> Load()
    {
        try
        {
            if (File.Exists(AppPaths.RecentFile))
                return JsonSerializer.Deserialize(File.ReadAllText(AppPaths.RecentFile), AppJsonContext.Default.ListRecentEntry) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("Could not read recent files", ex);
        }
        return [];
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.RecentFile, JsonSerializer.Serialize(_entries, AppJsonContext.Default.ListRecentEntry));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save recent files", ex);
        }
    }
}

internal static class TimeFormat
{
    /// <summary>Formats seconds as m:ss or h:mm:ss (hours shown when the reference duration is ≥ 1 h).</summary>
    public static string Format(double seconds, double referenceDuration = 0)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
        bool negative = seconds < 0;
        var t = TimeSpan.FromSeconds(Math.Floor(Math.Abs(seconds)));
        bool hours = t.TotalHours >= 1 || referenceDuration >= 3600;
        string s = hours ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        return negative ? "-" + s : s;
    }
}
