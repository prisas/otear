using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Otear.Mpv;

namespace Otear.Services;

public enum AppTheme { System, Light, Dark }
public enum ControlBarStyle { Floating, Bottom }
public enum ClickAction { PlayPause, ToggleControls, Nothing }
public enum HdrMode { Auto, AlwaysSdr }

/// <summary>User preferences. Every property raises PropertyChanged and is saved automatically.</summary>
public sealed partial class AppSettings : ObservableObject
{
    // ---- General
    private AppTheme _theme = AppTheme.System;
    public AppTheme Theme { get => _theme; set => SetProperty(ref _theme, value); }

    private ControlBarStyle _controlBarStyle = ControlBarStyle.Floating;
    public ControlBarStyle ControlBarStyle { get => _controlBarStyle; set => SetProperty(ref _controlBarStyle, value); }

    private double? _controlBarX, _controlBarY;
    /// <summary>
    /// Where the user dragged the floating control bar, as fractions (0–1) of the room it can move in, so it keeps
    /// its place relative to the window when that is resized or goes full screen. Null = default (bottom centre).
    /// </summary>
    public double? ControlBarX { get => _controlBarX; set => SetProperty(ref _controlBarX, value is { } v ? Math.Clamp(v, 0, 1) : null); }
    public double? ControlBarY { get => _controlBarY; set => SetProperty(ref _controlBarY, value is { } v ? Math.Clamp(v, 0, 1) : null); }

    [JsonIgnore]
    public bool HasCustomControlBarPosition => ControlBarX is not null && ControlBarY is not null;

    public void ResetControlBarPosition()
    {
        ControlBarX = null;
        ControlBarY = null;
    }

    private double _autoHideSeconds = 1.5;
    public double AutoHideSeconds { get => _autoHideSeconds; set => SetProperty(ref _autoHideSeconds, Math.Clamp(value, 1, 30)); }

    private ClickAction _clickAction = ClickAction.PlayPause;
    public ClickAction ClickAction { get => _clickAction; set => SetProperty(ref _clickAction, value); }

    private bool _resumePlayback = true;
    public bool ResumePlayback { get => _resumePlayback; set => SetProperty(ref _resumePlayback, value); }

    private bool _autoFitWindow = true;
    public bool AutoFitWindow { get => _autoFitWindow; set => SetProperty(ref _autoFitWindow, value); }

    private bool _lockAspectRatio = true;
    public bool LockAspectRatio { get => _lockAspectRatio; set => SetProperty(ref _lockAspectRatio, value); }

    private bool _showRemainingTime;
    public bool ShowRemainingTime { get => _showRemainingTime; set => SetProperty(ref _showRemainingTime, value); }

    // ---- Video
    private VideoQuality _quality = VideoQuality.Balanced;
    public VideoQuality Quality { get => _quality; set => SetProperty(ref _quality, value); }

    private HdrMode _hdrMode = HdrMode.Auto;
    public HdrMode HdrMode { get => _hdrMode; set => SetProperty(ref _hdrMode, value); }

    private PeakDetectionMode _hdrPeakDetection = PeakDetectionMode.Auto;
    /// <summary>libplacebo HDR peak detection: Auto = only when HDR must be tone-mapped (it is costly).</summary>
    public PeakDetectionMode HdrPeakDetection { get => _hdrPeakDetection; set => SetProperty(ref _hdrPeakDetection, value); }

    private bool _hardwareDecoding = true;
    public bool HardwareDecoding { get => _hardwareDecoding; set => SetProperty(ref _hardwareDecoding, value); }

    private bool _deband;
    public bool Deband { get => _deband; set => SetProperty(ref _deband, value); }

    private bool _smoothMotion;
    public bool SmoothMotion { get => _smoothMotion; set => SetProperty(ref _smoothMotion, value); }

    private bool _seekThumbnails = true;
    public bool SeekThumbnails { get => _seekThumbnails; set => SetProperty(ref _seekThumbnails, value); }

    // ---- Audio
    private string _audioDevice = "auto";
    public string AudioDevice { get => _audioDevice; set => SetProperty(ref _audioDevice, value ?? "auto"); }

    private bool _audioExclusive;
    public bool AudioExclusive { get => _audioExclusive; set => SetProperty(ref _audioExclusive, value); }

    private bool _audioPassthrough;
    public bool AudioPassthrough { get => _audioPassthrough; set => SetProperty(ref _audioPassthrough, value); }

    private double _volume = 100;
    public double Volume { get => _volume; set => SetProperty(ref _volume, value); }

    private bool _muted;
    public bool Muted { get => _muted; set => SetProperty(ref _muted, value); }

    private int _volumeMax = 130;
    public int VolumeMax { get => _volumeMax; set => SetProperty(ref _volumeMax, Math.Clamp(value, 100, 400)); }

    private string _preferredAudioLanguages = string.Empty;
    public string PreferredAudioLanguages { get => _preferredAudioLanguages; set => SetProperty(ref _preferredAudioLanguages, value ?? string.Empty); }

    // ---- Subtitles
    private string _preferredSubtitleLanguages = string.Empty;
    public string PreferredSubtitleLanguages { get => _preferredSubtitleLanguages; set => SetProperty(ref _preferredSubtitleLanguages, value ?? string.Empty); }

    private SubtitleAutoLoad _subtitleAutoLoad = SubtitleAutoLoad.Fuzzy;
    public SubtitleAutoLoad SubtitleAutoLoad { get => _subtitleAutoLoad; set => SetProperty(ref _subtitleAutoLoad, value); }

    private string _subtitleFont = string.Empty;
    public string SubtitleFont { get => _subtitleFont; set => SetProperty(ref _subtitleFont, value ?? string.Empty); }

    private int _subtitleFontSize = 44;
    public int SubtitleFontSize { get => _subtitleFontSize; set => SetProperty(ref _subtitleFontSize, Math.Clamp(value, 12, 120)); }

    private string _subtitleColor = "#FFFFFFFF";
    public string SubtitleColor { get => _subtitleColor; set => SetProperty(ref _subtitleColor, value); }

    private string _subtitleBorderColor = "#FF000000";
    public string SubtitleBorderColor { get => _subtitleBorderColor; set => SetProperty(ref _subtitleBorderColor, value); }

    private double _subtitleBorderSize = 3;
    public double SubtitleBorderSize { get => _subtitleBorderSize; set => SetProperty(ref _subtitleBorderSize, Math.Clamp(value, 0, 10)); }

    private int _subtitlePosition = 100;
    /// <summary>Vertical subtitle position in % of the picture height: 100 = bottom (default), 0 = top.</summary>
    public int SubtitlePosition { get => _subtitlePosition; set => SetProperty(ref _subtitlePosition, Math.Clamp(value, 0, 100)); }

    // ---- Advanced
    private bool _useMpvConfigDirectory;
    public bool UseMpvConfigDirectory { get => _useMpvConfigDirectory; set => SetProperty(ref _useMpvConfigDirectory, value); }

    private string _extraMpvOptions = string.Empty;
    public string ExtraMpvOptions { get => _extraMpvOptions; set => SetProperty(ref _extraMpvOptions, value ?? string.Empty); }

    private bool _debugLogging;
    public bool DebugLogging { get => _debugLogging; set => SetProperty(ref _debugLogging, value); }

    /// <summary>Maps the preferences to mpv options.</summary>
    public PlayerOptions ToPlayerOptions(int compositionWidth, int compositionHeight, bool safeMode = false) => new()
    {
        CompositionWidth = compositionWidth,
        CompositionHeight = compositionHeight,
        HardwareDecoding = HardwareDecoding,
        Quality = Quality,
        Deband = Deband,
        SmoothMotion = SmoothMotion,
        AudioDevice = AudioDevice,
        AudioExclusive = AudioExclusive,
        AudioPassthrough = AudioPassthrough,
        Volume = Volume,
        Muted = Muted,
        VolumeMax = VolumeMax,
        PreferredAudioLanguages = PreferredAudioLanguages,
        PreferredSubtitleLanguages = PreferredSubtitleLanguages,
        SubtitleAutoLoad = SubtitleAutoLoad,
        SubtitleFont = SubtitleFont,
        SubtitleFontSize = SubtitleFontSize,
        SubtitleColor = SubtitleColor,
        SubtitleBorderColor = SubtitleBorderColor,
        SubtitleBorderSize = SubtitleBorderSize,
        SubtitlePosition = SubtitlePosition,
        ResumePlayback = ResumePlayback,
        WatchLaterDirectory = AppPaths.WatchLater,
        ShaderCacheDirectory = AppPaths.ShaderCache,
        ScreenshotDirectory = AppPaths.Screenshots,
        ConfigDirectory = !safeMode && UseMpvConfigDirectory ? AppPaths.MpvConfig : null,
        LogFile = DebugLogging ? AppPaths.MpvLog : null,
        ExtraOptions = safeMode ? null : ExtraMpvOptions,
    };
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<RecentEntry>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>Loads and (debounced) saves <see cref="AppSettings"/>.</summary>
internal sealed class SettingsService : IDisposable
{
    private readonly Timer _saveTimer;
    public AppSettings Settings { get; }

    public SettingsService()
    {
        Settings = Load();
        _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        Settings.PropertyChanged += (_, _) => _saveTimer.Change(600, Timeout.Infinite);
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                using var stream = File.OpenRead(AppPaths.SettingsFile);
                if (JsonSerializer.Deserialize(stream, AppJsonContext.Default.AppSettings) is { } s) return s;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, AppJsonContext.Default.AppSettings));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Save();
    }
}
