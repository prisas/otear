using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Otear.Mpv;
using Otear.Player;
using Otear.Services;

namespace Otear.Controls;

/// <summary>The on-screen controller: transport, volume, track/speed menus, panels, seek bar.</summary>
public sealed partial class OscBar : UserControl
{
    private PlayerController? _player;
    private AppSettings? _settings;
    private bool _syncingVolume;
    private bool _pointerOver;
    private int _openFlyouts;

    public event Action? QuickSettingsRequested;
    public event Action? PlaylistRequested;
    public event Action? FullscreenRequested;
    public event Action? OpenSubtitleRequested;

    private static readonly double[] s_speedPresets = [0.5, 0.75, 1, 1.25, 1.5, 2];
    private bool _syncingSpeed;

    public OscBar()
    {
        InitializeComponent();
        for (int i = 0; i < s_speedPresets.Length; i++)
        {
            double speed = s_speedPresets[i];
            var preset = new Button
            {
                Content = $"{speed:0.##}×",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 4, 0, 5),
            };
            AutomationProperties.SetName(preset, $"Speed {speed:0.##}×");
            preset.Click += (_, _) => _player?.SetSpeed(speed);
            Grid.SetColumn(preset, i);
            SpeedPresets.Children.Add(preset);
        }
        foreach (var f in new FlyoutBase[] { AudioFlyout, SubtitleFlyout, SpeedFlyout })
        {
            f.Opened += (_, _) => _openFlyouts++;
            f.Closed += (_, _) => _openFlyouts = Math.Max(0, _openFlyouts - 1);
        }
        Seek.SeekRequested += (t, exact) => _player?.SeekTo(t, exact);
    }

    /// <summary>True while the user is using the bar (hovering, dragging, or a menu is open).</summary>
    public bool IsInteracting => _pointerOver || _openFlyouts > 0 || Seek.IsDragging || Seek.IsHovering || _dragStart is not null;

    public SeekBar SeekBar => Seek;

    /// <summary>Opens the audio-track menu (used by the UI benchmark).</summary>
    internal FlyoutBase OpenAudioMenu()
    {
        AudioButton.Flyout.ShowAt(AudioButton);
        return AudioButton.Flyout;
    }

    public void Attach(PlayerController player, AppSettings settings)
    {
        _player = player;
        _settings = settings;
        player.PropertyChanged += OnPlayerChanged;
        settings.PropertyChanged += OnSettingsChanged;
        VolumeSlider.Maximum = settings.VolumeMax;
        RefreshAll();
    }

    public void SetFullscreen(bool fullscreen)
    {
        FullscreenIcon.Glyph = fullscreen ? "" : "";
        ToolTipService.SetToolTip(FullscreenButton, fullscreen ? "Exit full screen (F / Esc)" : "Full screen (F)");
    }

    public void ApplyStyle(ControlBarStyle style)
    {
        // Only the floating bar can be moved; the bottom bar is docked to the window edge.
        _movable = style != ControlBarStyle.Bottom;
        Grip.Visibility = _movable ? Visibility.Visible : Visibility.Collapsed;
        if (style == ControlBarStyle.Bottom)
        {
            Panel.CornerRadius = new CornerRadius(0);
            Panel.BorderThickness = new Thickness(0, 1, 0, 0);
            Panel.Padding = new Thickness(16, 6, 16, 10);
        }
        else
        {
            Panel.CornerRadius = new CornerRadius(12);
            Panel.BorderThickness = new Thickness(1);
            Panel.Padding = new Thickness(14, 10, 14, 8); // top padding holds the grip
        }
    }

    // ------------------------------------------------------------------ moving the bar

    /// <summary>The user started dragging the bar.</summary>
    public event Action? MoveStarted;
    /// <summary>Total pointer offset (DIPs) since the drag started.</summary>
    public event Action<double, double>? Moved;
    public event Action? MoveCompleted;
    public event Action? ResetPositionRequested;

    private bool _movable = true;
    private Windows.Foundation.Point? _dragStart;
    private bool _dragging;

    private void OnPanelPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_movable || _dragStart is not null) return;
        // Only the grip and the bar's empty background start a move: buttons, sliders and the seek bar keep theirs.
        if (!ReferenceEquals(e.OriginalSource, Panel) && !IsWithin(e.OriginalSource as DependencyObject, Grip)) return;
        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;

        // Double-click on the grip: back to the default place. (Detected here because the pointer capture below
        // keeps WinUI from raising DoubleTapped on the grip.)
        long now = Environment.TickCount64;
        if (IsWithin(e.OriginalSource as DependencyObject, Grip) && now - _lastGripPress <= Interop.Win32.GetDoubleClickTime())
        {
            _lastGripPress = 0;
            ResetPositionRequested?.Invoke();
            return;
        }
        _lastGripPress = IsWithin(e.OriginalSource as DependencyObject, Grip) ? now : 0;

        if (!Panel.CapturePointer(e.Pointer)) return;
        _dragStart = point.Position;
        _dragging = false;
    }

    private long _lastGripPress;

    private void OnPanelMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetCurrentPoint(null).Position;
        double dx = p.X - start.X, dy = p.Y - start.Y;
        if (!_dragging)
        {
            // A click (or the double-click that resets) must not turn into a tiny move.
            if (dx * dx + dy * dy < 16) return;
            _dragging = true;
            MoveStarted?.Invoke();
        }
        Moved?.Invoke(dx, dy);
        e.Handled = true;
    }

    private void OnPanelReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is null) return;
        Panel.ReleasePointerCapture(e.Pointer);
        EndMove();
        e.Handled = true;
    }

    private void OnPanelCaptureLost(object sender, PointerRoutedEventArgs e) => EndMove();

    private void EndMove()
    {
        if (_dragStart is null) return;
        _dragStart = null;
        if (!_dragging) return;
        _dragging = false;
        MoveCompleted?.Invoke();
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (var d = element; d is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    /// <summary>Hides secondary buttons when the bar is narrow (level 0 = full, 1 = compact, 2 = minimal).</summary>
    public void SetCompact(int level)
    {
        var v1 = level >= 1 ? Visibility.Collapsed : Visibility.Visible;
        var v2 = level >= 2 ? Visibility.Collapsed : Visibility.Visible;
        PreviousButton.Visibility = NextButton.Visibility = v1;
        SpeedButton.Visibility = v1;
        VolumeSlider.Visibility = v1;
        AudioButton.Visibility = QuickSettingsButton.Visibility = PlaylistButton.Visibility = v2;
        BackButton.Visibility = ForwardButton.Visibility = v2;
        UpdatePlaylistButtons();
    }

    private void RefreshAll()
    {
        if (_player is null) return;
        UpdatePlayIcon();
        UpdateVolume();
        UpdateTime();
        UpdateSpeed();
        UpdateTrackButtons();
        UpdatePlaylistButtons();
        Seek.SetChapters(_player.Chapters);
        Seek.SetBuffered(_player.SeekRanges);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ShowRemainingTime)) UpdateTime(force: true);
        else if (e.PropertyName == nameof(AppSettings.VolumeMax) && _settings is not null) VolumeSlider.Maximum = _settings.VolumeMax;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerController.IsPaused):
            case nameof(PlayerController.EofReached):
                UpdatePlayIcon();
                break;
            case nameof(PlayerController.HasMedia):
                // The icon depends on HasMedia too: without this, a file that starts playing (pause stays
                // false) kept showing the "play" icon from before it was opened.
                UpdatePlayIcon();
                UpdateTrackButtons();
                break;
            case nameof(PlayerController.Position):
            case nameof(PlayerController.Duration):
                UpdateTime();
                break;
            case nameof(PlayerController.Volume):
            case nameof(PlayerController.IsMuted):
                UpdateVolume();
                break;
            case nameof(PlayerController.Speed):
                UpdateSpeed();
                break;
            case nameof(PlayerController.Tracks):
                UpdateTrackButtons();
                break;
            case nameof(PlayerController.Playlist):
            case nameof(PlayerController.PlaylistPosition):
                UpdatePlaylistButtons();
                break;
            case nameof(PlayerController.Chapters):
                Seek.SetChapters(_player!.Chapters);
                UpdatePlaylistButtons();
                break;
            case nameof(PlayerController.SeekRanges):
                Seek.SetBuffered(_player!.SeekRanges);
                break;
        }
    }

    private void UpdatePlayIcon()
    {
        var p = _player!;
        bool showPlay = p.IsPaused || !p.HasMedia;
        PlayIcon.Glyph = p.EofReached && p.IsPaused ? "" : showPlay ? "" : "";
        AutomationProperties.SetName(PlayButton, showPlay ? "Play" : "Pause");
    }

    private bool _live = true;
    private long _shownSecond = long.MinValue;
    private double _shownDuration = double.NaN;
    private bool _shownRemaining;

    /// <summary>
    /// While the bar is hidden it skips per-frame updates (nothing would be visible) and catches up
    /// as soon as it is shown again.
    /// </summary>
    public bool IsLive
    {
        get => _live;
        set
        {
            if (_live == value) return;
            _live = value;
            if (value) UpdateTime(force: true);
        }
    }

    private void UpdateTime(bool force = false)
    {
        if (!_live && !force) return;
        PerfCounters.OscTimeUpdates++;
        var p = _player!;
        Seek.Update(p.Position, p.Duration);

        // Text only changes once per second: skip the string formatting and layout in between.
        long second = (long)Math.Floor(p.Position);
        bool remaining = _settings?.ShowRemainingTime == true;
        if (!force && second == _shownSecond && p.Duration.Equals(_shownDuration) && remaining == _shownRemaining) return;
        _shownSecond = second;
        _shownDuration = p.Duration;
        _shownRemaining = remaining;
        ElapsedText.Text = TimeFormat.Format(p.Position, p.Duration);
        if (p.Duration <= 0)
            RemainingText.Text = p.HasMedia ? "Live" : "0:00";
        else if (_settings?.ShowRemainingTime == true)
            RemainingText.Text = TimeFormat.Format(-(p.Duration - p.Position), p.Duration);
        else
            RemainingText.Text = TimeFormat.Format(p.Duration, p.Duration);
    }

    private void UpdateVolume()
    {
        var p = _player!;
        _syncingVolume = true;
        VolumeSlider.Value = p.Volume;
        _syncingVolume = false;
        MuteIcon.Glyph = p.IsMuted ? "" : p.Volume <= 0 ? "" : p.Volume < 34 ? "" : p.Volume < 67 ? "" : "";
    }

    private void UpdateSpeed()
    {
        double s = _player!.Speed;
        SpeedText.Text = $"{s:0.##}×";
        SpeedValueText.Text = $"{s:0.00}×";
        if (Math.Abs(SpeedSlider.Value - s) > 1e-6)
        {
            _syncingSpeed = true;
            SpeedSlider.Value = s;
            _syncingSpeed = false;
        }
    }

    private void UpdateTrackButtons()
    {
        var p = _player!;
        AudioButton.IsEnabled = p.TracksOf(MpvTrackType.Audio).Any();
        SubtitleButton.IsEnabled = p.HasMedia;
    }

    private void UpdatePlaylistButtons()
    {
        var p = _player!;
        bool multi = p.Playlist.Count > 1;
        bool chapters = p.Chapters.Count > 1;
        PreviousButton.IsEnabled = (multi && p.PlaylistPosition > 0) || chapters;
        NextButton.IsEnabled = (multi && p.PlaylistPosition < p.Playlist.Count - 1) || chapters;
    }

    // ------------------------------------------------------------------ handlers

    private void OnPanelPointerEntered(object sender, PointerRoutedEventArgs e) => _pointerOver = true;
    private void OnPanelPointerExited(object sender, PointerRoutedEventArgs e) => _pointerOver = false;

    private void OnPlayPause(object sender, RoutedEventArgs e) => _player?.TogglePause();
    private void OnBack(object sender, RoutedEventArgs e) => _player?.SeekRelative(-10);
    private void OnForward(object sender, RoutedEventArgs e) => _player?.SeekRelative(10);

    private void OnPrevious(object sender, RoutedEventArgs e)
    {
        var p = _player!;
        if (p.Chapters.Count > 1 && !(p.Playlist.Count > 1 && p.Chapter <= 0)) p.PreviousChapter();
        else p.PlaylistPrevious();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        var p = _player!;
        if (p.Chapters.Count > 1 && !(p.Playlist.Count > 1 && p.Chapter >= p.Chapters.Count - 1)) p.NextChapter();
        else p.PlaylistNext();
    }

    private void OnMute(object sender, RoutedEventArgs e) => _player?.ToggleMute();

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingVolume || _player is null) return;
        _player.SetVolume(e.NewValue);
    }

    private void OnToggleRemaining(object sender, RoutedEventArgs e)
    {
        if (_settings is not null) _settings.ShowRemainingTime = !_settings.ShowRemainingTime;
    }

    private void OnQuickSettings(object sender, RoutedEventArgs e) => QuickSettingsRequested?.Invoke();
    private void OnPlaylist(object sender, RoutedEventArgs e) => PlaylistRequested?.Invoke();
    private void OnFullscreen(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke();

    private void OnAudioFlyoutOpening(object? sender, object e)
    {
        AudioFlyout.Items.Clear();
        TrackMenus.FillTracks(AudioFlyout.Items, _player!, MpvTrackType.Audio, includeOff: false);
    }

    private void OnSubtitleFlyoutOpening(object? sender, object e)
    {
        SubtitleFlyout.Items.Clear();
        TrackMenus.FillSubtitles(SubtitleFlyout.Items, _player!, () => OpenSubtitleRequested?.Invoke());
    }

    private void OnSpeedFlyoutOpening(object? sender, object e) => UpdateSpeed();

    // Focus on the slider, so the arrow keys move it in 0.05 steps straight away.
    private void OnSpeedFlyoutOpened(object? sender, object e) => SpeedSlider.Focus(FocusState.Programmatic);

    private void OnSpeedSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSpeed || _player is null || Math.Abs(e.NewValue - _player.Speed) < 0.001) return;
        _player.SetSpeed(e.NewValue);
    }

    private void OnSlower(object sender, RoutedEventArgs e) => _player?.StepSpeed(-1);
    private void OnFaster(object sender, RoutedEventArgs e) => _player?.StepSpeed(1);
}
