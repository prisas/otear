using System.Globalization;

namespace Otear.Mpv;

/// <summary>Rendering quality preset. PowerSaving uses mpv's "fast" profile (bilinear scaling, no dithering,
/// no peak detection): roughly a quarter of the GPU time of Balanced, for laptops on battery or weak GPUs.</summary>
public enum VideoQuality { Balanced, High, PowerSaving }
public enum SubtitleAutoLoad { Exact, Fuzzy, All, None }

/// <summary>
/// Everything the app wants to configure in mpv, independent of the UI layer.
/// <see cref="Build"/> turns it into the option list passed before mpv_initialize.
/// </summary>
public sealed record PlayerOptions
{
    public int CompositionWidth { get; init; } = 1280;
    public int CompositionHeight { get; init; } = 720;

    public bool HardwareDecoding { get; init; } = true;
    public VideoQuality Quality { get; init; } = VideoQuality.Balanced;
    public bool Deband { get; init; }
    public bool SmoothMotion { get; init; }

    public string AudioDevice { get; init; } = "auto";
    public bool AudioExclusive { get; init; }
    public bool AudioPassthrough { get; init; }
    public double Volume { get; init; } = 100;
    public bool Muted { get; init; }
    public int VolumeMax { get; init; } = 130;
    public string? PreferredAudioLanguages { get; init; }

    public string? PreferredSubtitleLanguages { get; init; }
    public SubtitleAutoLoad SubtitleAutoLoad { get; init; } = SubtitleAutoLoad.Fuzzy;
    public string? SubtitleFont { get; init; }
    public int SubtitleFontSize { get; init; } = 44;
    public string SubtitleColor { get; init; } = "#FFFFFFFF";
    public string SubtitleBorderColor { get; init; } = "#FF000000";
    public double SubtitleBorderSize { get; init; } = 3;
    /// <summary>mpv <c>sub-pos</c>: vertical position in % of the picture height (0 = top, 100 = bottom, the default).</summary>
    public int SubtitlePosition { get; init; } = 100;

    public bool ResumePlayback { get; init; } = true;
    public required string WatchLaterDirectory { get; init; }
    public required string ShaderCacheDirectory { get; init; }
    public required string ScreenshotDirectory { get; init; }

    /// <summary>When set, mpv reads mpv.conf/input.conf/scripts from this directory.</summary>
    public string? ConfigDirectory { get; init; }
    public string? LogFile { get; init; }

    /// <summary>Extra "name=value" lines added last (advanced users).</summary>
    public string? ExtraOptions { get; init; }

    /// <summary>Options that are always set, whatever the user configures.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Fixed { get; } =
    [
        new("vo", "gpu-next"),
        new("gpu-api", "d3d11"),
        new("gpu-context", "d3d11"),
        new("d3d11-output-mode", "composition"),
        new("idle", "yes"),
        new("keep-open", "yes"),
        new("force-window", "no"),
        new("terminal", "no"),
        new("osc", "no"),
        new("osd-bar", "no"),
        new("input-default-bindings", "no"),
        new("input-vo-keyboard", "no"),
        new("input-cursor", "no"),
        new("load-stats-overlay", "yes"),
        new("load-console", "no"),
        new("load-auto-profiles", "no"),
        new("background-color", "#000000"),
        new("audio-display", "embedded-first"),
        new("screenshot-format", "png"),
        new("screenshot-template", "%F-%wH.%wM.%wS"),
        new("hr-seek-framedrop", "yes"),
        new("cursor-autohide", "no"),
        new("watch-later-options", "start,vid,aid,sid,secondary-sid,sub-delay,audio-delay,speed,sub-visibility"),
        new("demuxer-max-bytes", "200MiB"),
        new("demuxer-max-back-bytes", "100MiB"),
        new("ytdl", "yes"),
    ];

    public IReadOnlyList<KeyValuePair<string, string>> Build()
    {
        var o = new List<KeyValuePair<string, string>>(Fixed);
        void Add(string k, string v) => o.Add(new(k, v));

        if (ConfigDirectory is { Length: > 0 })
        {
            Add("config", "yes");
            Add("config-dir", ConfigDirectory);
            Add("load-scripts", "yes");
        }
        else
        {
            Add("config", "no");
            Add("load-scripts", "no");
        }

        Add("d3d11-composition-size", $"{Math.Max(16, CompositionWidth)}x{Math.Max(16, CompositionHeight)}");
        Add("hwdec", HardwareDecoding ? "auto-safe" : "no");
        if (Quality == VideoQuality.High) Add("profile", "high-quality");
        else if (Quality == VideoQuality.PowerSaving) Add("profile", "fast");
        Add("deband", Deband ? "yes" : "no");
        if (SmoothMotion)
        {
            Add("video-sync", "display-resample");
            Add("interpolation", "yes");
            Add("tscale", "oversample");
        }

        Add("ao", "wasapi");
        Add("audio-device", string.IsNullOrWhiteSpace(AudioDevice) ? "auto" : AudioDevice);
        Add("audio-exclusive", AudioExclusive ? "yes" : "no");
        if (AudioPassthrough) Add("audio-spdif", "ac3,eac3,dts,dts-hd,truehd");
        Add("volume", Format(Math.Clamp(Volume, 0, VolumeMax)));
        Add("volume-max", VolumeMax.ToString(CultureInfo.InvariantCulture));
        Add("mute", Muted ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(PreferredAudioLanguages)) Add("alang", PreferredAudioLanguages!);

        if (!string.IsNullOrWhiteSpace(PreferredSubtitleLanguages)) Add("slang", PreferredSubtitleLanguages!);
        Add("sub-auto", SubtitleAutoLoad switch
        {
            SubtitleAutoLoad.Exact => "exact",
            SubtitleAutoLoad.All => "all",
            SubtitleAutoLoad.None => "no",
            _ => "fuzzy",
        });
        if (!string.IsNullOrWhiteSpace(SubtitleFont)) Add("sub-font", SubtitleFont!);
        Add("sub-font-size", SubtitleFontSize.ToString(CultureInfo.InvariantCulture));
        Add("sub-color", SubtitleColor);
        Add("sub-border-color", SubtitleBorderColor);
        Add("sub-border-size", Format(SubtitleBorderSize));
        Add("sub-pos", Math.Clamp(SubtitlePosition, 0, 100).ToString(CultureInfo.InvariantCulture));

        Add("resume-playback", ResumePlayback ? "yes" : "no");
        Add("watch-later-dir", WatchLaterDirectory);
        Add("gpu-shader-cache-dir", ShaderCacheDirectory);
        Add("screenshot-dir", ScreenshotDirectory);

        if (LogFile is { Length: > 0 })
        {
            Add("log-file", LogFile);
            Add("msg-level", "all=v");
        }

        foreach (var (k, v) in ParseExtra(ExtraOptions)) Add(k, v);
        return o;
    }

    /// <summary>Parses "name=value" lines (blank lines and lines starting with # are ignored).</summary>
    public static IEnumerable<KeyValuePair<string, string>> ParseExtra(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("--", StringComparison.Ordinal)) line = line[2..];
            int eq = line.IndexOf('=');
            yield return eq < 0 ? new(line, "yes") : new(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
    }

    private static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
