using System.Globalization;

namespace Otear.Mpv;

public enum MpvTrackType { Video, Audio, Sub, Unknown }

/// <summary>One entry of mpv's "track-list" property.</summary>
public sealed record MpvTrack(
    long Id,
    MpvTrackType Type,
    string? Title,
    string? Lang,
    string? Codec,
    string? CodecDescription,
    bool IsDefault,
    bool IsForced,
    bool IsExternal,
    string? ExternalFilename,
    bool IsSelected,
    bool IsAlbumArt,
    bool IsImage,
    long? Width,
    long? Height,
    double? Fps,
    long? ChannelCount,
    string? Channels,
    long? SampleRate,
    bool HearingImpaired,
    long? DolbyVisionProfile = null)
{
    /// <summary>Human readable name, e.g. "English · Commentary (AAC 2ch)".</summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>(3);
            string? lang = LanguageName(Lang);
            if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title!);
            if (lang is not null && (Title is null || !Title.Contains(lang, StringComparison.OrdinalIgnoreCase)))
                parts.Insert(0, lang);
            if (parts.Count == 0) parts.Add($"Track {Id}");
            string name = string.Join(" · ", parts);

            var detail = new List<string>(3);
            if (!string.IsNullOrEmpty(Codec)) detail.Add(Codec!.ToUpperInvariant());
            if (Type == MpvTrackType.Audio && ChannelCount is > 0) detail.Add($"{ChannelCount}ch");
            if (Type == MpvTrackType.Video && Width is > 0 && Height is > 0) detail.Add($"{Width}×{Height}");
            if (IsForced) detail.Add("forced");
            if (IsExternal) detail.Add("external");
            return detail.Count == 0 ? name : $"{name} ({string.Join(", ", detail)})";
        }
    }

    private static readonly Lazy<Dictionary<string, string>> s_languages = new(BuildLanguageMap);

    // ISO 639-2/B (bibliographic) codes that differ from the /T codes .NET knows.
    private static readonly Dictionary<string, string> s_bibliographic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alb"] = "sqi", ["arm"] = "hye", ["baq"] = "eus", ["bur"] = "mya", ["chi"] = "zho", ["cze"] = "ces",
        ["dut"] = "nld", ["fre"] = "fra", ["geo"] = "kat", ["ger"] = "deu", ["gre"] = "ell", ["ice"] = "isl",
        ["mac"] = "mkd", ["mao"] = "mri", ["may"] = "msa", ["per"] = "fas", ["rum"] = "ron", ["slo"] = "slk",
        ["tib"] = "bod", ["wel"] = "cym",
    };

    private static Dictionary<string, string> BuildLanguageMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (string.IsNullOrEmpty(c.Name)) continue;
            map.TryAdd(c.TwoLetterISOLanguageName, c.EnglishName);
            map.TryAdd(c.ThreeLetterISOLanguageName, c.EnglishName);
        }
        return map;
    }

    /// <summary>Builds the language-name table ahead of first use (it enumerates all OS cultures).</summary>
    public static void PrewarmLanguageNames() => _ = s_languages.Value;

    /// <summary>Maps an ISO 639-1/639-2 language code (as stored in containers) to an English language name.</summary>
    public static string? LanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code is "und" or "zxx" or "mul") return null;
        string key = code.Split('-', '_')[0];
        if (s_bibliographic.TryGetValue(key, out var t)) key = t;
        return s_languages.Value.TryGetValue(key, out var name) ? name : code;
    }

    internal static MpvTrack FromNode(IReadOnlyDictionary<string, object?> m) => new(
        Id: m.Int("id") ?? 0,
        Type: m.Str("type") switch { "video" => MpvTrackType.Video, "audio" => MpvTrackType.Audio, "sub" => MpvTrackType.Sub, _ => MpvTrackType.Unknown },
        Title: m.Str("title"),
        Lang: m.Str("lang"),
        Codec: m.Str("codec"),
        CodecDescription: m.Str("codec-desc"),
        IsDefault: m.Flag("default"),
        IsForced: m.Flag("forced"),
        IsExternal: m.Flag("external"),
        ExternalFilename: m.Str("external-filename"),
        IsSelected: m.Flag("selected"),
        IsAlbumArt: m.Flag("albumart"),
        IsImage: m.Flag("image"),
        Width: m.Int("demux-w"),
        Height: m.Int("demux-h"),
        Fps: m.Dbl("demux-fps"),
        ChannelCount: m.Int("demux-channel-count"),
        Channels: m.Str("demux-channels"),
        SampleRate: m.Int("demux-samplerate"),
        HearingImpaired: m.Flag("hearing-impaired"),
        DolbyVisionProfile: m.Int("dolby-vision-profile"));

    public static IReadOnlyList<MpvTrack> ParseList(object? node)
        => node is List<object?> list
            ? list.OfType<IReadOnlyDictionary<string, object?>>().Select(FromNode).ToArray()
            : [];
}

/// <summary>One entry of "chapter-list".</summary>
public sealed record MpvChapter(int Index, string Title, double Time)
{
    public static IReadOnlyList<MpvChapter> ParseList(object? node)
    {
        if (node is not List<object?> list) return [];
        var result = new List<MpvChapter>(list.Count);
        int i = 0;
        foreach (var item in list.OfType<IReadOnlyDictionary<string, object?>>())
        {
            result.Add(new MpvChapter(i, item.Str("title") is { Length: > 0 } t ? t : $"Chapter {i + 1}", item.Dbl("time") ?? 0));
            i++;
        }
        return result;
    }
}

/// <summary>One entry of "playlist".</summary>
public sealed record MpvPlaylistEntry(int Index, long Id, string Filename, string? Title, bool IsCurrent, bool IsPlaying)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title! : SafeFileName(Filename);

    private static string SafeFileName(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile) return path;
        try { return Path.GetFileName(path) is { Length: > 0 } n ? n : path; } catch { return path; }
    }

    public static IReadOnlyList<MpvPlaylistEntry> ParseList(object? node)
    {
        if (node is not List<object?> list) return [];
        var result = new List<MpvPlaylistEntry>(list.Count);
        int i = 0;
        foreach (var item in list.OfType<IReadOnlyDictionary<string, object?>>())
        {
            result.Add(new MpvPlaylistEntry(i, item.Int("id") ?? 0, item.Str("filename") ?? string.Empty,
                item.Str("title"), item.Flag("current"), item.Flag("playing")));
            i++;
        }
        return result;
    }
}

/// <summary>Parsed "video-params" / "video-out-params".</summary>
public sealed record MpvVideoParams(
    long Width, long Height, long DisplayWidth, long DisplayHeight,
    string? PixelFormat, string? HwPixelFormat, string? ColorMatrix, string? ColorLevels,
    string? Primaries, string? Gamma, double? SigPeak, long Rotate,
    double? MaxCll, double? MaxFall, double? MaxLuma)
{
    public bool IsHdr => Gamma is "pq" or "hlg";

    public string DynamicRangeName => Gamma switch
    {
        "pq" => "HDR10",
        "hlg" => "HLG",
        _ => "SDR",
    };

    public static MpvVideoParams? Parse(object? node)
    {
        if (node is not IReadOnlyDictionary<string, object?> m) return null;
        return new MpvVideoParams(
            m.Int("w") ?? 0, m.Int("h") ?? 0, m.Int("dw") ?? m.Int("w") ?? 0, m.Int("dh") ?? m.Int("h") ?? 0,
            m.Str("pixelformat"), m.Str("hw-pixelformat"), m.Str("colormatrix"), m.Str("colorlevels"),
            m.Str("primaries"), m.Str("gamma"), m.Dbl("sig-peak"), m.Int("rotate") ?? 0,
            m.Dbl("max-cll"), m.Dbl("max-fall"), m.Dbl("max-luma"));
    }
}

/// <summary>Seekable ranges from "demuxer-cache-state".</summary>
public readonly record struct MpvSeekRange(double Start, double End)
{
    public static IReadOnlyList<MpvSeekRange> ParseCacheState(object? node)
    {
        if (node is not IReadOnlyDictionary<string, object?> m || m.TryGetValue("seekable-ranges", out var r) is false || r is not List<object?> list)
            return [];
        return list.OfType<IReadOnlyDictionary<string, object?>>()
            .Select(x => new MpvSeekRange(x.Dbl("start") ?? 0, x.Dbl("end") ?? 0))
            .ToArray();
    }
}

internal static class NodeReader
{
    public static string? Str(this IReadOnlyDictionary<string, object?> m, string key)
        => m.TryGetValue(key, out var v) ? v as string : null;

    public static long? Int(this IReadOnlyDictionary<string, object?> m, string key)
        => m.TryGetValue(key, out var v) ? v switch { long l => l, double d => (long)d, bool b => b ? 1 : 0, _ => null } : null;

    public static double? Dbl(this IReadOnlyDictionary<string, object?> m, string key)
        => m.TryGetValue(key, out var v) ? v switch { double d => d, long l => l, _ => null } : null;

    public static bool Flag(this IReadOnlyDictionary<string, object?> m, string key)
        => m.TryGetValue(key, out var v) && v is true;
}
