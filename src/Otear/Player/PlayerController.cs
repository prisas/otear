using System.Collections.Concurrent;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Otear.Mpv;
using Otear.Mpv.Native;
using Otear.Services;

namespace Otear.Player;

/// <summary>
/// UI-facing wrapper around one mpv core. mpv property changes arrive on mpv's event thread, are coalesced
/// (latest value wins) and applied on the UI thread, so every public property here is UI-thread state.
/// </summary>
public sealed partial class PlayerController : ObservableObject, IDisposable
{
    private readonly DispatcherQueue _dq;
    private readonly AppSettings _settings;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _ready;
    private int _started;
    private MpvClient? _mpv;
    private readonly ConcurrentDictionary<string, object?> _pending = new(StringComparer.Ordinal);
    private int _flushQueued;
    private volatile bool _disposed;
    private int _compW = 1280, _compH = 720;
    private (double Time, bool Exact)? _pendingSeek;
    private bool _seekInFlight;
    private bool _resumeChecked;
    private bool _hdrOutput;
    private double _displayPeak;
    private string _peakDetect = string.Empty;
    private string? _chromaScaler;

    /// <summary>mpv created a (new) swap chain; bind it to the SwapChainPanel. UI thread.</summary>
    public event Action<nint>? SwapChainChanged;
    /// <summary>Video output (re)configured with these parameters. UI thread.</summary>
    public event Action<MpvVideoParams?>? VideoReconfigured;
    public event Action? FileStarted;
    public event Action? FileLoaded;
    /// <summary>Playback (re)started after loading or seeking: a frame is ready. UI thread.</summary>
    public event Action? PlaybackRestarted;
    public event Action<string>? PlaybackFailed;
    /// <summary>Short feedback text for the on-screen toast ("Volume 80%").</summary>
    public event Action<string>? OsdMessage;

    public PlayerController(DispatcherQueue dispatcher, AppSettings settings)
    {
        _dq = dispatcher;
        _settings = settings;
        _ready = _readyTcs.Task;
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.HdrPeakDetection)) UpdatePeakDetection();
        };
    }

    public Task Ready => _ready;

    /// <summary>
    /// Starts mpv on a background thread (called at launch, so it loads while the window is being built).
    /// Commands issued before it is ready simply wait for <see cref="Ready"/>.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        StartupTrace.Mark("mpvStart");
        Task.Run(() =>
        {
            try
            {
                Initialize();
                _readyTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                Log.Error("mpv failed to start", ex);
                _readyTcs.TrySetException(ex);
            }
        });
    }

    // ------------------------------------------------------------------ state (UI thread)

    private bool _isReady;
    public bool IsReady { get => _isReady; private set => SetProperty(ref _isReady, value); }

    private bool _hasMedia;
    /// <summary>A file is loaded (or loading); false when mpv is idle.</summary>
    public bool HasMedia { get => _hasMedia; private set => SetProperty(ref _hasMedia, value); }

    private bool _isLoaded;
    public bool IsLoaded { get => _isLoaded; private set => SetProperty(ref _isLoaded, value); }

    private bool _isPaused;
    public bool IsPaused { get => _isPaused; private set => SetProperty(ref _isPaused, value); }

    private bool _isBuffering;
    public bool IsBuffering { get => _isBuffering; private set => SetProperty(ref _isBuffering, value); }

    private bool _isSeeking;
    public bool IsSeeking { get => _isSeeking; private set => SetProperty(ref _isSeeking, value); }

    private bool _eofReached;
    public bool EofReached { get => _eofReached; private set => SetProperty(ref _eofReached, value); }

    private double _position;
    public double Position { get => _position; private set => SetProperty(ref _position, value); }

    private double _duration;
    public double Duration { get => _duration; private set => SetProperty(ref _duration, value); }

    private double _volume = 100;
    public double Volume { get => _volume; private set => SetProperty(ref _volume, value); }

    private bool _isMuted;
    public bool IsMuted { get => _isMuted; private set => SetProperty(ref _isMuted, value); }

    private double _speed = 1;
    public double Speed { get => _speed; private set => SetProperty(ref _speed, value); }

    private string _mediaTitle = string.Empty;
    public string MediaTitle { get => _mediaTitle; private set => SetProperty(ref _mediaTitle, value); }

    private string? _path;
    public string? Path { get => _path; private set => SetProperty(ref _path, value); }

    private IReadOnlyList<MpvTrack> _tracks = [];
    public IReadOnlyList<MpvTrack> Tracks { get => _tracks; private set => SetProperty(ref _tracks, value); }

    private bool _hasVideo;
    /// <summary>The file has a real video track (not just cover art).</summary>
    public bool HasVideo { get => _hasVideo; private set => SetProperty(ref _hasVideo, value); }

    private long? _videoId, _audioId, _subtitleId, _secondarySubtitleId;
    public long? VideoId { get => _videoId; private set => SetProperty(ref _videoId, value); }
    public long? AudioId { get => _audioId; private set => SetProperty(ref _audioId, value); }
    public long? SubtitleId { get => _subtitleId; private set => SetProperty(ref _subtitleId, value); }
    public long? SecondarySubtitleId { get => _secondarySubtitleId; private set => SetProperty(ref _secondarySubtitleId, value); }

    private IReadOnlyList<MpvChapter> _chapters = [];
    public IReadOnlyList<MpvChapter> Chapters { get => _chapters; private set => SetProperty(ref _chapters, value); }

    private int _chapter = -1;
    public int Chapter { get => _chapter; private set => SetProperty(ref _chapter, value); }

    private IReadOnlyList<MpvPlaylistEntry> _playlist = [];
    public IReadOnlyList<MpvPlaylistEntry> Playlist { get => _playlist; private set => SetProperty(ref _playlist, value); }

    private int _playlistPosition = -1;
    public int PlaylistPosition { get => _playlistPosition; private set => SetProperty(ref _playlistPosition, value); }

    private IReadOnlyList<MpvSeekRange> _seekRanges = [];
    public IReadOnlyList<MpvSeekRange> SeekRanges { get => _seekRanges; private set => SetProperty(ref _seekRanges, value); }

    private MpvVideoParams? _videoParams;
    public MpvVideoParams? VideoParams { get => _videoParams; private set => SetProperty(ref _videoParams, value); }

    private string? _hwdecCurrent;
    public string? HwdecCurrent { get => _hwdecCurrent; private set => SetProperty(ref _hwdecCurrent, value); }

    private IReadOnlyDictionary<string, object?> _metadata = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> Metadata { get => _metadata; private set => SetProperty(ref _metadata, value); }

    private double _subtitleDelay, _audioDelay, _subtitleScale = 1, _subtitlePosition = 100;
    public double SubtitleDelay { get => _subtitleDelay; private set => SetProperty(ref _subtitleDelay, value); }
    public double AudioDelay { get => _audioDelay; private set => SetProperty(ref _audioDelay, value); }
    public double SubtitleScale { get => _subtitleScale; private set => SetProperty(ref _subtitleScale, value); }
    public double SubtitlePosition { get => _subtitlePosition; private set => SetProperty(ref _subtitlePosition, value); }

    private bool _subtitlesVisible = true;
    public bool SubtitlesVisible { get => _subtitlesVisible; private set => SetProperty(ref _subtitlesVisible, value); }

    private string _loopFile = "no";
    public string LoopFile { get => _loopFile; private set => SetProperty(ref _loopFile, value); }

    private IReadOnlyDictionary<string, double> _equalizer = new Dictionary<string, double>();
    /// <summary>brightness/contrast/saturation/gamma/hue (−100…100).</summary>
    public IReadOnlyDictionary<string, double> VideoEqualizer { get => _equalizer; private set => SetProperty(ref _equalizer, value); }

    private string _aspectOverride = "no";
    public string AspectOverride { get => _aspectOverride; private set => SetProperty(ref _aspectOverride, value); }

    private long _rotation;
    public long Rotation { get => _rotation; private set => SetProperty(ref _rotation, value); }

    private string _audioDevice = "auto";
    public string AudioDevice { get => _audioDevice; private set => SetProperty(ref _audioDevice, value); }

    private IReadOnlyList<(string Name, string Description)> _audioDevices = [];
    public IReadOnlyList<(string Name, string Description)> AudioDevices { get => _audioDevices; private set => SetProperty(ref _audioDevices, value); }

    private bool _deinterlace;
    public bool Deinterlace { get => _deinterlace; private set => SetProperty(ref _deinterlace, value); }

    private string _hwdecMode = "auto-safe";
    /// <summary>The requested hardware-decoding mode ("no" when off); see <see cref="HwdecCurrent"/> for what is in use.</summary>
    public string HwdecMode { get => _hwdecMode; private set => SetProperty(ref _hwdecMode, value); }

    private string _subAssOverride = "yes";
    public string SubAssOverride { get => _subAssOverride; private set => SetProperty(ref _subAssOverride, value); }

    public string? MpvVersion { get; private set; }
    public string? FfmpegVersion { get; private set; }
    public string? LibplaceboVersion { get; private set; }

    public string? Artist => Metadata.FirstOrDefault(kv => kv.Key.Equals("artist", StringComparison.OrdinalIgnoreCase)).Value as string;
    public string? Album => Metadata.FirstOrDefault(kv => kv.Key.Equals("album", StringComparison.OrdinalIgnoreCase)).Value as string;

    public IEnumerable<MpvTrack> TracksOf(MpvTrackType type) => Tracks.Where(t => t.Type == type);

    // ------------------------------------------------------------------ init

    private static readonly (string Name, MpvClient.ObserveFormat Format)[] s_observed =
    [
        ("idle-active", MpvClient.ObserveFormat.Flag),
        ("pause", MpvClient.ObserveFormat.Flag),
        ("paused-for-cache", MpvClient.ObserveFormat.Flag),
        ("seeking", MpvClient.ObserveFormat.Flag),
        ("eof-reached", MpvClient.ObserveFormat.Flag),
        ("time-pos", MpvClient.ObserveFormat.Double),
        ("duration", MpvClient.ObserveFormat.Double),
        ("volume", MpvClient.ObserveFormat.Double),
        ("mute", MpvClient.ObserveFormat.Flag),
        ("speed", MpvClient.ObserveFormat.Double),
        ("media-title", MpvClient.ObserveFormat.String),
        ("path", MpvClient.ObserveFormat.String),
        ("track-list", MpvClient.ObserveFormat.Node),
        ("vid", MpvClient.ObserveFormat.Int64),
        ("aid", MpvClient.ObserveFormat.Int64),
        ("sid", MpvClient.ObserveFormat.Int64),
        ("secondary-sid", MpvClient.ObserveFormat.Int64),
        ("chapter-list", MpvClient.ObserveFormat.Node),
        ("chapter", MpvClient.ObserveFormat.Int64),
        ("playlist", MpvClient.ObserveFormat.Node),
        ("playlist-pos", MpvClient.ObserveFormat.Int64),
        ("demuxer-cache-state", MpvClient.ObserveFormat.Node),
        ("video-params", MpvClient.ObserveFormat.Node),
        ("hwdec-current", MpvClient.ObserveFormat.String),
        ("metadata", MpvClient.ObserveFormat.Node),
        ("sub-delay", MpvClient.ObserveFormat.Double),
        ("audio-delay", MpvClient.ObserveFormat.Double),
        ("sub-scale", MpvClient.ObserveFormat.Double),
        ("sub-pos", MpvClient.ObserveFormat.Double),
        ("sub-visibility", MpvClient.ObserveFormat.Flag),
        ("loop-file", MpvClient.ObserveFormat.String),
        ("brightness", MpvClient.ObserveFormat.Double),
        ("contrast", MpvClient.ObserveFormat.Double),
        ("saturation", MpvClient.ObserveFormat.Double),
        ("gamma", MpvClient.ObserveFormat.Double),
        ("hue", MpvClient.ObserveFormat.Double),
        ("video-aspect-override", MpvClient.ObserveFormat.String),
        ("video-rotate", MpvClient.ObserveFormat.Int64),
        ("audio-device", MpvClient.ObserveFormat.String),
        ("audio-device-list", MpvClient.ObserveFormat.Node),
        ("deinterlace", MpvClient.ObserveFormat.String),
        ("hwdec", MpvClient.ObserveFormat.String),
        ("sub-ass-override", MpvClient.ObserveFormat.String),
    ];

    private void Initialize()
    {
        MpvClient mpv;
        try
        {
            mpv = MpvClient.Create(_settings.ToPlayerOptions(_compW, _compH).Build(), _settings.DebugLogging ? "v" : "warn");
        }
        catch (Exception ex)
        {
            // A bad custom option or mpv.conf must not make the player unusable: retry without them.
            Log.Error("mpv failed to start with user options, retrying in safe mode", ex);
            mpv = MpvClient.Create(_settings.ToPlayerOptions(_compW, _compH, safeMode: true).Build(), "warn");
            _dq.TryEnqueue(() => OsdMessage?.Invoke("Some custom mpv options were invalid and were ignored"));
        }

        mpv.LogMessage += OnLog;
        mpv.PropertyChanged += c => Enqueue(c.Name, c.Value);
        mpv.EventRaised += OnSimpleEvent;
        mpv.StartFile += _ => Post(() =>
        {
            _resumeChecked = false;
            IsLoaded = false;
            HasMedia = true;
            FileStarted?.Invoke();
        });
        mpv.EndFile += OnEndFile;

        foreach (var (name, format) in s_observed) mpv.Observe(name, format);

        MpvVersion = mpv.GetString("mpv-version");
        FfmpegVersion = mpv.GetString("ffmpeg-version");
        LibplaceboVersion = mpv.GetString("libplacebo-version");
        Log.Info($"{MpvVersion}, FFmpeg {FfmpegVersion}, libplacebo {LibplaceboVersion}");

        _mpv = mpv;
        StartupTrace.Mark("mpvReady");
        // The panel may have reported a different size while we were starting.
        mpv.Post("set", "d3d11-composition-size", $"{_compW}x{_compH}");
        Post(() => IsReady = true);
    }

    private void OnLog(MpvLogMessage m)
    {
        // Expected in composition mode (there is no window/output to query); HdrDisplayService covers it.
        if (m.Text.StartsWith("Failed to query swap chain's output information", StringComparison.Ordinal)) return;
        if (m.Level <= MpvLogLevel.Warn) Log.Write(m.Level <= MpvLogLevel.Error ? "MPV-ERR" : "MPV-WARN", $"[{m.Prefix}] {m.Text}");
    }

    private void OnSimpleEvent(MpvSimpleEvent e)
    {
        var mpv = _mpv;
        if (mpv is null || _disposed) return;
        switch (e.Id)
        {
            case MpvEventId.VideoReconfig:
            {
                // Read on the event thread: cheap, and keeps the UI thread free.
                long sc = mpv.GetInt64("display-swapchain") ?? 0;
                var vp = MpvVideoParams.Parse(mpv.GetNode("video-params"));
                Post(() =>
                {
                    if (sc != 0) SwapChainChanged?.Invoke((nint)sc);
                    VideoReconfigured?.Invoke(vp);
                });
                break;
            }
            case MpvEventId.FileLoaded:
            {
                double pos = mpv.GetDouble("time-pos") ?? 0;
                StartupTrace.Mark("fileLoaded");
                Post(() =>
                {
                    IsLoaded = true;
                    FileLoaded?.Invoke();
                    if (!_resumeChecked)
                    {
                        _resumeChecked = true;
                        if (pos > 1) OsdMessage?.Invoke($"Resumed at {TimeFormat.Format(pos, Duration)}");
                    }
                });
                break;
            }
            case MpvEventId.PlaybackRestart:
                Post(() =>
                {
                    FinishSeek();
                    PlaybackRestarted?.Invoke();
                });
                break;
        }
    }

    private void OnEndFile(MpvEndFile e)
    {
        if (e.IsError)
        {
            string msg = e.ErrorMessage;
            Log.Warn($"Playback failed: {msg}");
            Post(() => PlaybackFailed?.Invoke(msg));
        }
    }

    // ------------------------------------------------------------------ coalesced property delivery

    private long _positionIntervalMs;
    private long _lastPositionTick;

    /// <summary>
    /// How often playback-position changes may wake the UI thread. 0 = every frame (seek bar visible);
    /// a larger value when nothing on screen shows the position, so the UI thread stays idle during playback.
    /// Other property changes are always delivered immediately (and carry the latest position with them).
    /// </summary>
    public void SetPositionUpdateInterval(TimeSpan interval)
    {
        Interlocked.Exchange(ref _positionIntervalMs, (long)interval.TotalMilliseconds);
        if (interval <= TimeSpan.Zero && _pending.ContainsKey("time-pos")) ScheduleFlush();
    }

    private void Enqueue(string name, object? value)
    {
        if (_disposed) return;
        _pending[name] = value;
        if (name == "time-pos")
        {
            long interval = Interlocked.Read(ref _positionIntervalMs);
            if (interval > 0 && Environment.TickCount64 - Interlocked.Read(ref _lastPositionTick) < interval) return; // stays pending
        }
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _flushQueued, 1) == 0)
            _dq.TryEnqueue(DispatcherQueuePriority.Normal, Flush);
    }

    private void Flush()
    {
        Interlocked.Exchange(ref _flushQueued, 0);
        if (_disposed) return;
        PerfCounters.Flushes++;
        foreach (var key in _pending.Keys)
        {
            if (!_pending.TryRemove(key, out var value)) continue;
            if (key == "time-pos") Interlocked.Exchange(ref _lastPositionTick, Environment.TickCount64);
            PerfCounters.Applies++;
            try { Apply(key, value); }
            catch (Exception ex) { Log.Error($"Applying '{key}' failed", ex); }
        }
    }

    private static double D(object? v, double fallback = 0) => v switch { double d => d, long l => l, _ => fallback };
    private static long? L(object? v) => v switch { long l => l, double d => (long)d, _ => null };

    private void Apply(string name, object? v)
    {
        switch (name)
        {
            case "idle-active":
                HasMedia = v is not true;
                if (!HasMedia) ResetMediaState();
                break;
            case "pause": IsPaused = v is true; break;
            case "paused-for-cache": IsBuffering = v is true; break;
            case "seeking": IsSeeking = v is true; break;
            case "eof-reached": EofReached = v is true; break;
            case "time-pos": Position = D(v); break;
            case "duration": Duration = D(v); break;
            case "volume": Volume = D(v, 100); break;
            case "mute": IsMuted = v is true; break;
            case "speed": Speed = D(v, 1); break;
            case "media-title": MediaTitle = v as string ?? string.Empty; break;
            case "path": Path = v as string; break;
            case "track-list":
                Tracks = MpvTrack.ParseList(v);
                HasVideo = Tracks.Any(t => t.Type == MpvTrackType.Video && !t.IsAlbumArt);
                UpdatePeakDetection();
                break;
            case "vid": VideoId = L(v); break;
            case "aid": AudioId = L(v); break;
            case "sid": SubtitleId = L(v); break;
            case "secondary-sid": SecondarySubtitleId = L(v); break;
            case "chapter-list": Chapters = MpvChapter.ParseList(v); break;
            case "chapter": Chapter = (int)(L(v) ?? -1); break;
            case "playlist": Playlist = MpvPlaylistEntry.ParseList(v); break;
            case "playlist-pos": PlaylistPosition = (int)(L(v) ?? -1); break;
            case "demuxer-cache-state": SeekRanges = MpvSeekRange.ParseCacheState(v); break;
            case "video-params":
                VideoParams = MpvVideoParams.Parse(v);
                UpdatePeakDetection();
                UpdateChromaScaler();
                break;
            case "hwdec-current": HwdecCurrent = v as string; break;
            case "metadata":
                Metadata = v as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
                OnPropertyChanged(nameof(Artist));
                OnPropertyChanged(nameof(Album));
                break;
            case "sub-delay": SubtitleDelay = D(v); break;
            case "audio-delay": AudioDelay = D(v); break;
            case "sub-scale": SubtitleScale = D(v, 1); break;
            case "sub-pos": SubtitlePosition = D(v, 100); break;
            case "sub-visibility": SubtitlesVisible = v is not false; break;
            case "loop-file": LoopFile = v as string ?? "no"; break;
            case "brightness" or "contrast" or "saturation" or "gamma" or "hue":
            {
                var eq = new Dictionary<string, double>(VideoEqualizer) { [name] = D(v) };
                VideoEqualizer = eq;
                break;
            }
            case "video-aspect-override": AspectOverride = v as string ?? "no"; break;
            case "video-rotate": Rotation = L(v) ?? 0; break;
            case "audio-device": AudioDevice = v as string ?? "auto"; break;
            case "deinterlace": Deinterlace = v as string == "yes"; break;
            case "hwdec": HwdecMode = v as string ?? "no"; break;
            case "sub-ass-override": SubAssOverride = v as string ?? "yes"; break;
            case "audio-device-list":
                AudioDevices = v is List<object?> list
                    ? list.OfType<IReadOnlyDictionary<string, object?>>()
                        .Select(d => (d.TryGetValue("name", out var n) ? n as string ?? "" : "", d.TryGetValue("description", out var de) ? de as string ?? "" : ""))
                        .ToArray()
                    : [];
                break;
        }
    }

    private void ResetMediaState()
    {
        IsLoaded = false;
        Position = 0;
        Duration = 0;
        Chapters = [];
        SeekRanges = [];
        VideoParams = null;
        HasVideo = false;
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        _dq.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { action(); }
            catch (Exception ex) { Log.Error("UI callback failed", ex); }
        });
    }

    // ------------------------------------------------------------------ commands

    private MpvClient? Mpv => _disposed ? null : _mpv;

    private void Run(params string[] args) => Mpv?.Post(args);

    private void Set(string name, string value)
    {
        var mpv = Mpv;
        if (mpv is null) return;
        _ = mpv.SetPropertyAsync(name, value).ContinueWith(
            t => Log.Warn($"set {name}={value} failed: {t.Exception?.InnerException?.Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private static string F(double v) => MpvClient.Format(v);

    /// <summary>Opens files/URLs. The first replaces the current file unless <paramref name="append"/> is set.</summary>
    /// <param name="startPaused">Load and prepare the first frame without starting playback (call <see cref="Play"/> later).</param>
    public async Task OpenAsync(IReadOnlyList<string> items, bool append = false, bool startPaused = false)
    {
        if (items.Count == 0) return;
        await _ready;
        var mpv = Mpv;
        if (mpv is null) return;
        if (!append) SaveResumeState();
        if (startPaused) _ = mpv.SetPropertyAsync("pause", true);
        var tasks = new List<Task>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            string mode = append ? "append-play" : i == 0 ? "replace" : "append";
            tasks.Add(mpv.CommandAsync("loadfile", items[i], mode));
        }
        if (!append && !startPaused) _ = mpv.SetPropertyAsync("pause", false);
        try { await Task.WhenAll(tasks); }
        catch (Exception ex) { Log.Warn($"loadfile failed: {ex.Message}"); }
    }

    public void TogglePause()
    {
        if (EofReached && IsPaused)
        {
            // Replay from the start, like most players do after the end.
            Run("seek", "0", "absolute");
            Set("pause", "no");
            return;
        }
        Run("cycle", "pause");
    }

    public void Play() => Set("pause", "no");
    public void Pause() => Set("pause", "yes");

    public void SeekRelative(double seconds, bool exact = true)
    {
        Run("seek", F(seconds), exact ? "relative+exact" : "relative+keyframes");
        IsSeeking = true;
    }

    /// <summary>Absolute seek that coalesces rapid requests (seek-bar dragging): at most one seek in flight.</summary>
    public void SeekTo(double seconds, bool exact)
    {
        _pendingSeek = (Math.Max(0, seconds), exact);
        Position = Math.Max(0, seconds);
        if (!_seekInFlight) _ = PumpSeeksAsync();
    }

    private async Task PumpSeeksAsync()
    {
        var mpv = Mpv;
        if (mpv is null) return;
        _seekInFlight = true;
        try
        {
            while (_pendingSeek is { } s)
            {
                _pendingSeek = null;
                try { await mpv.CommandAsync("seek", F(s.Time), s.Exact ? "absolute+exact" : "absolute+keyframes"); }
                catch (Exception ex) { Log.Warn($"seek failed: {ex.Message}"); }
            }
        }
        finally
        {
            _seekInFlight = false;
        }
    }

    private void FinishSeek() => IsSeeking = false;

    public void SetVolume(double volume, bool showOsd = false)
    {
        volume = Math.Clamp(Math.Round(volume), 0, _settings.VolumeMax);
        Set("volume", F(volume));
        Volume = volume;
        _settings.Volume = volume;
        if (IsMuted && volume > 0) SetMuted(false);
        if (showOsd) OsdMessage?.Invoke($"Volume {volume:0}%");
    }

    public void ChangeVolume(double delta) => SetVolume(Volume + delta, showOsd: true);

    public void SetMuted(bool muted, bool showOsd = false)
    {
        Set("mute", muted ? "yes" : "no");
        IsMuted = muted;
        _settings.Muted = muted;
        if (showOsd) OsdMessage?.Invoke(muted ? "Muted" : $"Volume {Volume:0}%");
    }

    public void ToggleMute() => SetMuted(!IsMuted, showOsd: true);

    /// <summary>Range and step of the speed sliders, [ ] and Ctrl+wheel.</summary>
    public const double MinSpeed = 0.25, MaxSpeed = 4, SpeedStep = 0.05;

    public void SetSpeed(double speed)
    {
        speed = Math.Clamp(Math.Round(speed, 2), 0.1, 16);
        Set("speed", F(speed));
        // Shown at once (and read back by StepSpeed), without waiting for mpv's property notification.
        Speed = speed;
        OsdMessage?.Invoke($"Speed {speed:0.00}×");
    }

    /// <summary>
    /// Changes the speed by <paramref name="steps"/> × 0.05 (1.00 → 1.05 → 1.10 …), on the same grid as the
    /// speed sliders and within their 0.25–4× range.
    /// </summary>
    public void StepSpeed(int steps)
    {
        double target = Math.Clamp(Math.Round(Speed / SpeedStep) * SpeedStep + steps * SpeedStep, MinSpeed, MaxSpeed);
        // A speed outside the range (set in mpv.conf) only moves back towards it.
        if (steps > 0 ? target <= Speed : target >= Speed) return;
        SetSpeed(target);
    }

    public void SelectTrack(MpvTrackType type, long? id)
    {
        string prop = type switch { MpvTrackType.Video => "vid", MpvTrackType.Audio => "aid", _ => "sid" };
        Set(prop, id?.ToString(CultureInfo.InvariantCulture) ?? "no");
        var track = Tracks.FirstOrDefault(t => t.Id == id && t.Type == type);
        string kind = type switch { MpvTrackType.Video => "Video", MpvTrackType.Audio => "Audio", _ => "Subtitles" };
        OsdMessage?.Invoke($"{kind}: {track?.DisplayName ?? "Off"}");
    }

    public void SelectSecondarySubtitle(long? id) =>
        Set("secondary-sid", id?.ToString(CultureInfo.InvariantCulture) ?? "no");

    public void CycleSubtitles()
    {
        var subs = TracksOf(MpvTrackType.Sub).ToList();
        if (subs.Count == 0) { OsdMessage?.Invoke("No subtitles"); return; }
        int idx = subs.FindIndex(s => s.Id == SubtitleId);
        SelectTrack(MpvTrackType.Sub, idx + 1 < subs.Count ? subs[idx + 1].Id : null);
    }

    public void CycleAudio()
    {
        var audio = TracksOf(MpvTrackType.Audio).ToList();
        if (audio.Count < 2) return;
        int idx = audio.FindIndex(s => s.Id == AudioId);
        SelectTrack(MpvTrackType.Audio, audio[(idx + 1) % audio.Count].Id);
    }

    public void ToggleSubtitleVisibility()
    {
        bool visible = !SubtitlesVisible;
        Set("sub-visibility", visible ? "yes" : "no");
        OsdMessage?.Invoke(visible ? "Subtitles shown" : "Subtitles hidden");
    }

    public async Task AddSubtitleAsync(string path)
    {
        var mpv = Mpv;
        if (mpv is null) return;
        try
        {
            await mpv.CommandAsync("sub-add", path, "select");
            OsdMessage?.Invoke($"Subtitles: {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            OsdMessage?.Invoke("Could not load subtitles");
            Log.Warn($"sub-add failed: {ex.Message}");
        }
    }

    public void AdjustSubtitleDelay(double delta)
    {
        double d = Math.Round(SubtitleDelay + delta, 3);
        SetSubtitleDelay(d);
    }

    public void SetSubtitleDelay(double seconds)
    {
        Set("sub-delay", F(seconds));
        OsdMessage?.Invoke($"Subtitle delay {seconds * 1000:+0;-0;0} ms");
    }

    public void SetAudioDelay(double seconds)
    {
        Set("audio-delay", F(seconds));
        OsdMessage?.Invoke($"Audio delay {seconds * 1000:+0;-0;0} ms");
    }

    public void SetSubtitleScale(double scale) => Set("sub-scale", F(Math.Clamp(scale, 0.1, 5)));
    /// <summary>Moves subtitles (0 = top, 100 = bottom) and remembers it for the next files and sessions.</summary>
    public void SetSubtitlePosition(double pos)
    {
        int value = (int)Math.Round(Math.Clamp(pos, 0, 100));
        Set("sub-pos", value.ToString(CultureInfo.InvariantCulture));
        _settings.SubtitlePosition = value;
    }
    public void SetVideoEqualizer(string name, double value) => Set(name, F(Math.Clamp(Math.Round(value), -100, 100)));
    public void SetAspectOverride(string value) => Set("video-aspect-override", value);
    public void SetRotation(long degrees) => Set("video-rotate", ((degrees % 360 + 360) % 360).ToString(CultureInfo.InvariantCulture));
    public void SetAudioDevice(string name) { Set("audio-device", name); _settings.AudioDevice = name; }
    public void SetHardwareDecoding(bool on) => Set("hwdec", on ? "auto-safe" : "no");
    public void SetLoopFile(bool loop) { Set("loop-file", loop ? "inf" : "no"); OsdMessage?.Invoke(loop ? "Loop on" : "Loop off"); }

    /// <summary>Sets any mpv property (used by the quick-settings panel for less common options).</summary>
    public void SetRaw(string name, string value) => Set(name, value);
    public string? GetRaw(string name) => Mpv?.GetString(name);
    public object? GetNode(string name) => Mpv?.GetNode(name);

    public void FrameStep(bool backwards) => Run(backwards ? "frame-back-step" : "frame-step");

    public void NextChapter() => Run("add", "chapter", "1");
    public void PreviousChapter() => Run("add", "chapter", "-1");
    public void SetChapter(int index) => Set("chapter", index.ToString(CultureInfo.InvariantCulture));

    public void PlaylistNext() { SaveResumeState(); Run("playlist-next"); }
    public void PlaylistPrevious() { SaveResumeState(); Run("playlist-prev"); }
    public void PlaylistPlay(int index) { SaveResumeState(); Run("playlist-play-index", index.ToString(CultureInfo.InvariantCulture)); }
    public void PlaylistClear() => Run("playlist-clear");
    public void PlaylistRemove(int index) => Run("playlist-remove", index.ToString(CultureInfo.InvariantCulture));
    public void PlaylistMove(int from, int to) => Run("playlist-move", from.ToString(CultureInfo.InvariantCulture), to.ToString(CultureInfo.InvariantCulture));

    public async Task ScreenshotAsync(bool withSubtitles)
    {
        var mpv = Mpv;
        if (mpv is null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.Screenshots);
            await mpv.CommandAsync("screenshot", withSubtitles ? "subtitles" : "video");
            OsdMessage?.Invoke("Screenshot saved to Pictures\\Otear");
        }
        catch (Exception ex)
        {
            OsdMessage?.Invoke("Screenshot failed");
            Log.Warn($"screenshot failed: {ex.Message}");
        }
    }

    public void ToggleStats() => Run("script-binding", "stats/display-stats-toggle");

    /// <summary>Stops playback and returns mpv to idle (the welcome screen).</summary>
    public void Stop()
    {
        SaveResumeState();
        Run("stop");
    }

    /// <summary>Records the resume position (or forgets it when the file was finished).</summary>
    public void SaveResumeState()
    {
        var mpv = Mpv;
        if (mpv is null || !_settings.ResumePlayback || !HasMedia || !IsLoaded) return;
        bool finished = EofReached || (Duration > 0 && Position > Duration - 10);
        // Asynchronous, so a busy core (e.g. opening a stream) never blocks the UI. mpv runs commands in order,
        // so this still happens before a following loadfile/stop/quit.
        if (finished) mpv.Post("delete-watch-later-config");
        else if (Position > 10) mpv.Post("write-watch-later-config");
    }

    // ------------------------------------------------------------------ video surface plumbing

    /// <summary>Physical pixel size of the SwapChainPanel; mpv renders at exactly this size.</summary>
    public void SetCompositionSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == _compW && height == _compH)) return;
        _compW = width;
        _compH = height;
        Mpv?.Post("set", "d3d11-composition-size", $"{width}x{height}");
        UpdateChromaScaler();
    }

    /// <summary>
    /// Display color handling. HDR display: let libplacebo switch the swap chain to the content's color space
    /// (PQ/BT.2020 for HDR, sRGB for SDR) and tone-map down to the display's peak. SDR display: sRGB swap chain,
    /// HDR content is tone-mapped by libplacebo.
    /// </summary>
    public async Task ApplyDisplayColorAsync(bool hdrOutput, double peakNits)
    {
        await _ready;
        var mpv = Mpv;
        if (mpv is null) return;
        _hdrOutput = hdrOutput;
        _displayPeak = peakNits;
        UpdatePeakDetection();
        if (hdrOutput)
        {
            mpv.Post("set", "target-colorspace-hint", "yes");
            mpv.Post("set", "target-peak", peakNits > 100 ? F(Math.Round(peakNits)) : "auto");
        }
        else
        {
            mpv.Post("set", "target-colorspace-hint", "no");
            mpv.Post("set", "target-peak", "auto");
        }
        Log.Info($"Display color: {(hdrOutput ? $"HDR passthrough (peak {peakNits:0} nits)" : "SDR")}");
    }

    /// <summary>
    /// Turns libplacebo's per-frame HDR peak detection on only when it can change the picture
    /// (see <see cref="ToneMapping"/>): it is by far the most expensive part of HDR rendering.
    /// </summary>
    private void UpdatePeakDetection()
    {
        if (Mpv is null) return;
        bool dolbyVision = Tracks.Any(t => t.Type == MpvTrackType.Video && t.IsSelected && t.DolbyVisionProfile is not null);
        // Power saving keeps peak detection off whatever the content (that is what the fast profile is for).
        var mode = _settings.Quality == VideoQuality.PowerSaving ? PeakDetectionMode.Off : _settings.HdrPeakDetection;
        string value = ToneMapping.PeakDetectionValue(mode, VideoParams, dolbyVision, _hdrOutput, _displayPeak);
        if (value == _peakDetect) return;
        _peakDetect = value;
        Set("hdr-compute-peak", value);
        Log.Info($"HDR peak detection {value} ({VideoParams?.DynamicRangeName ?? "no video"}, " +
                 $"{(_hdrOutput ? $"HDR display {_displayPeak:0} nits" : "SDR output")}{(dolbyVision ? ", Dolby Vision" : "")})");
    }

    /// <summary>
    /// Uses a cheap chroma scaler when the picture is shown no larger than its colour planes, where the
    /// choice of filter cannot be seen (see <see cref="ScalerPolicy"/>). Left alone if the user set it.
    /// </summary>
    private void UpdateChromaScaler()
    {
        if (Mpv is null || _settings.UseMpvConfigDirectory || _settings.ExtraMpvOptions.Contains("cscale", StringComparison.OrdinalIgnoreCase)) return;
        string value = ScalerPolicy.ChromaScaler(VideoParams, _compW, _compH);
        if (value == _chromaScaler) return;
        _chromaScaler = value;
        Set("cscale", value);
        Log.Info($"Chroma scaler {(value.Length == 0 ? "default" : value)} (video {VideoParams?.Width}x{VideoParams?.Height} {VideoParams?.HwPixelFormat ?? VideoParams?.PixelFormat}, output {_compW}x{_compH})");
    }

    // ------------------------------------------------------------------ shutdown

    public void Dispose()
    {
        if (_disposed) return;
        try { SaveResumeState(); } catch (Exception ex) { Log.Error("Saving resume state failed", ex); }
        _disposed = true;
        var mpv = _mpv;
        _mpv = null;
        if (mpv is not null)
        {
            // Never let a stuck core hang the app on exit.
            if (!Task.Run(mpv.Dispose).Wait(TimeSpan.FromSeconds(4)))
                Log.Warn("mpv did not shut down within 4 s");
        }
    }
}
