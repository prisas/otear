using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Otear.Controls;
using Otear.Interop;
using Otear.Mpv;
using Otear.Player;
using Otear.Services;
using Windows.Foundation;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Otear.Views;

/// <summary>
/// Everything drawn over the video: control bar, title, toast, spinner, errors, side panels, plus mouse input
/// on the video and auto-hiding of the controls and cursor.
/// </summary>
public sealed partial class PlayerView : UserControl
{
    private PlayerController? _player;
    private AppSettings? _settings;
    private IPlayerHost? _host;
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _toastTimer;
    private readonly DispatcherQueueTimer _collapseTimer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private int _lastPointerX = -1_000_000, _lastPointerY = -1_000_000; // screen pixels
    private bool _controlsVisible = true;
    private bool _pressed, _dragged, _swallowRelease;
    private Point _pressPoint;
    private long _pressTicks;
    private readonly DispatcherQueueTimer _clickTimer;
    private enum Panel { None, QuickSettings, Playlist }
    private Panel _panel = Panel.None;

    public PlayerView()
    {
        InitializeComponent();
        var dq = DispatcherQueue.GetForCurrentThread();
        _hideTimer = dq.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(250);
        _hideTimer.Tick += (_, _) => CheckAutoHide();
        _toastTimer = dq.CreateTimer();
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) =>
        {
            if (Toast.Opacity > 0)
            {
                Toast.Opacity = 0;
                _toastTimer.Interval = TimeSpan.FromMilliseconds(260);
                _toastTimer.Start(); // collapse once faded
            }
            else
            {
                Toast.Visibility = Visibility.Collapsed;
            }
        };
        _collapseTimer = dq.CreateTimer();
        _collapseTimer.IsRepeating = false;
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(220);
        _collapseTimer.Tick += (_, _) => OnCollapseTimer();
        _clickTimer = dq.CreateTimer();
        _clickTimer.IsRepeating = false;
        _clickTimer.Tick += (_, _) => PerformClickAction();

        var fade = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        Osc.OpacityTransition = fade;
        TitleOverlay.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        Toast.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(220) };

        Osc.QuickSettingsRequested += () => TogglePanel(Panel.QuickSettings);
        Osc.PlaylistRequested += () => TogglePanel(Panel.Playlist);
        Osc.FullscreenRequested += () => _host?.ToggleFullscreen();
        Osc.OpenSubtitleRequested += () => _ = _host?.OpenSubtitleAsync();
        Osc.MoveStarted += OnOscMoveStarted;
        Osc.Moved += OnOscMoved;
        Osc.MoveCompleted += OnOscMoveCompleted;
        Osc.ResetPositionRequested += OnOscResetPosition;
        // The bar's height changes with its compact level: keep a moved bar inside the window.
        Osc.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Height != e.NewSize.Height && _settings?.HasCustomControlBarPosition == true) LayoutOsc(Root.ActualWidth);
        };
        ErrorBar.Closed += (_, _) => ErrorBar.IsOpen = false;
    }

    public OscBar ControlBar => Osc;

    private VideoSurface _input = null!;

    /// <summary>Takes mouse input for the video area from <paramref name="source"/>, and hides the pointer there.</summary>
    public void AttachInputSource(VideoSurface source)
    {
        _input = source;
        source.PointerPressed += OnInputPressed;
        source.PointerMoved += OnInputMoved;
        source.PointerMoved += OnRootPointerMoved;
        source.PointerReleased += OnInputReleased;
        source.PointerCanceled += OnInputCanceled;
        source.PointerCaptureLost += OnInputCanceled;
        source.DoubleTapped += OnInputDoubleTapped;
        source.RightTapped += OnInputRightTapped;
        source.PointerWheelChanged += OnInputWheel;
    }

    public void Attach(PlayerController player, AppSettings settings, IPlayerHost host)
    {
        _player = player;
        _settings = settings;
        _host = host;
        Osc.Attach(player, settings);
        QuickSettings.Attach(player, settings, host);
        PlaylistPanel.Attach(player, host);
        QuickSettings.CloseRequested += () => TogglePanel(Panel.None);
        PlaylistPanel.CloseRequested += () => TogglePanel(Panel.None);

        player.PropertyChanged += OnPlayerChanged;
        player.OsdMessage += ShowToast;
        player.PlaybackFailed += msg =>
        {
            ErrorBar.Message = msg;
            ErrorBar.IsOpen = true;
        };
        player.FileStarted += () => ErrorBar.IsOpen = false;
        settings.PropertyChanged += OnSettingsChanged;
        ApplyControlBarStyle();
        _hideTimer.Start();
    }

    // ------------------------------------------------------------------ state

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        var p = _player!;
        switch (e.PropertyName)
        {
            case nameof(PlayerController.MediaTitle):
                TitleText.Text = p.MediaTitle;
                UpdateAudioCard();
                break;
            case nameof(PlayerController.Tracks):
            case nameof(PlayerController.IsLoaded):
            case nameof(PlayerController.Artist):
            case nameof(PlayerController.HasMedia):
                UpdateAudioCard();
                UpdateSpinner();
                break;
            case nameof(PlayerController.IsBuffering):
                UpdateSpinner();
                break;
            case nameof(PlayerController.IsPaused):
                if (p.IsPaused) ShowControls();
                break;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ControlBarStyle):
                ApplyControlBarStyle();
                break;
            case nameof(AppSettings.ControlBarX) or nameof(AppSettings.ControlBarY) when !_movingOsc:
                LayoutOsc(Root.ActualWidth); // e.g. "Reset position" in Settings
                break;
        }
    }

    private void UpdateAudioCard()
    {
        var p = _player!;
        bool audioOnly = p.HasMedia && p.IsLoaded && !p.Tracks.Any(t => t.Type == MpvTrackType.Video);
        AudioCard.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
        AudioTitle.Text = p.MediaTitle;
        AudioArtist.Text = string.Join(" — ", new[] { p.Artist, p.Album }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private void UpdateSpinner()
    {
        var p = _player!;
        bool busy = p.HasMedia && (p.IsBuffering || !p.IsLoaded);
        Spinner.IsActive = busy;
        Spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyControlBarStyle()
    {
        var style = _settings?.ControlBarStyle ?? ControlBarStyle.Floating;
        Osc.ApplyStyle(style);
        LayoutOsc(Root.ActualWidth);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => LayoutOsc(e.NewSize.Width);

    private void LayoutOsc(double width)
    {
        if (width <= 0) return;
        bool bottom = _settings?.ControlBarStyle == ControlBarStyle.Bottom;
        if (bottom)
        {
            Osc.Width = double.NaN;
            Osc.HorizontalAlignment = HorizontalAlignment.Stretch;
            Osc.VerticalAlignment = VerticalAlignment.Bottom;
            Osc.Margin = new Thickness(0);
        }
        else
        {
            double room = width - SidePanelSpace(width);
            Osc.Width = Math.Min(720, Math.Max(300, room - 32));
            if (_settings is { HasCustomControlBarPosition: true } s)
            {
                // Where the user put it, as a fraction of the room it can move in.
                var area = OscArea();
                PlaceOsc(area.Left + s.ControlBarX!.Value * (area.Right - area.Left),
                         area.Top + s.ControlBarY!.Value * (area.Bottom - area.Top));
            }
            else
            {
                // With a side panel open, centre the floating bar in the space to the left of the panel
                // (in smaller windows the panel would otherwise cover the bar's right-hand buttons).
                Osc.HorizontalAlignment = HorizontalAlignment.Center;
                Osc.VerticalAlignment = VerticalAlignment.Bottom;
                Osc.Margin = new Thickness(16, 0, 16 + SidePanelSpace(width), width < 520 ? 12 : 24);
            }
        }
        double barWidth = bottom ? width : Osc.Width;
        Osc.SetCompact(barWidth < 460 ? 2 : barWidth < 690 ? 1 : 0);
        SidePanel.Width = Math.Min(360, Math.Max(260, width - 48));
    }

    private double SidePanelSpace(double width) => _panel != Panel.None ? Math.Min(360, Math.Max(260, width - 48)) + 12 : 0;

    // ------------------------------------------------------------------ moving the control bar

    private const double OscEdge = 8;
    private double _moveLeft, _moveTop;

    /// <summary>
    /// The range of the bar's top-left corner: inside the window, left of an open side panel and, in a window,
    /// below the title bar (whose drag area would swallow the bar's clicks).
    /// </summary>
    private (double Left, double Top, double Right, double Bottom) OscArea()
    {
        double width = Root.ActualWidth, height = Root.ActualHeight;
        double barWidth = double.IsNaN(Osc.Width) ? Osc.ActualWidth : Osc.Width;
        double barHeight = Osc.ActualHeight > 0 ? Osc.ActualHeight : 84;
        double top = _host?.IsFullscreen == true ? OscEdge : 48;
        double right = Math.Max(OscEdge, width - SidePanelSpace(width) - barWidth - OscEdge);
        double bottomEdge = Math.Max(top, height - barHeight - OscEdge);
        return (OscEdge, top, right, bottomEdge);
    }

    private void PlaceOsc(double left, double top)
    {
        var area = OscArea();
        Osc.HorizontalAlignment = HorizontalAlignment.Left;
        Osc.VerticalAlignment = VerticalAlignment.Top;
        Osc.Margin = new Thickness(Math.Clamp(left, area.Left, area.Right), Math.Clamp(top, area.Top, area.Bottom), 0, 0);
    }

    private void OnOscMoveStarted()
    {
        var origin = Osc.TransformToVisual(Root).TransformPoint(new Point(0, 0));
        _moveLeft = origin.X;
        _moveTop = origin.Y;
    }

    private void OnOscMoved(double dx, double dy)
    {
        PlaceOsc(_moveLeft + dx, _moveTop + dy);
        NotifyActivity();
    }

    private bool _movingOsc;

    private void OnOscMoveCompleted()
    {
        if (_settings is null) return;
        var area = OscArea();
        static double Fraction(double value, double from, double to) => to - from < 1 ? 0.5 : Math.Clamp((value - from) / (to - from), 0, 1);
        _movingOsc = true;
        try
        {
            _settings.ControlBarX = Fraction(Osc.Margin.Left, area.Left, area.Right);
            _settings.ControlBarY = Fraction(Osc.Margin.Top, area.Top, area.Bottom);
        }
        finally
        {
            _movingOsc = false;
        }
    }

    private void OnOscResetPosition()
    {
        _settings?.ResetControlBarPosition();
        LayoutOsc(Root.ActualWidth);
        NotifyActivity();
    }

    // ------------------------------------------------------------------ controls visibility

    /// <summary>
    /// Called on user activity: shows the controls and restarts the auto-hide delay. Keyboard shortcuts pass
    /// <paramref name="showPointer"/> false: they bring up the controls, but the mouse pointer only comes back
    /// when the mouse moves.
    /// </summary>
    public void NotifyActivity(bool showPointer = true)
    {
        _lastActivity = DateTime.UtcNow;
        ShowControls(showPointer);
    }

    private void ShowControls(bool showPointer = true)
    {
        if (showPointer) _input.IsCursorHidden = false;
        if (_controlsVisible) return;
        _controlsVisible = true;
        _collapseTimer.Stop();
        Osc.IsLive = true;
        _player?.SetPositionUpdateInterval(TimeSpan.Zero);
        Osc.Visibility = Visibility.Visible;
        TitleOverlay.Visibility = Visibility.Visible;
        Osc.Opacity = 1;
        Osc.IsHitTestVisible = true;
        TitleOverlay.Opacity = 1;
        _host?.OnControlsVisibilityChanged(true);
    }

    private void HideControls()
    {
        // Only applies while the pointer is over the video (the title bar and other windows keep theirs).
        _input.IsCursorHidden = true;
        if (!_controlsVisible) return;
        _controlsVisible = false;
        // Nothing on screen shows the position now: stop per-frame UI updates.
        Osc.IsLive = false;
        _player?.SetPositionUpdateInterval(TimeSpan.FromMilliseconds(500));
        Osc.Opacity = 0;
        Osc.IsHitTestVisible = false;
        TitleOverlay.Opacity = 0;
        // After the fade, take the overlays out of the visual tree: no acrylic blur to recompute for every
        // video frame, and nothing left on top of the video's swap chain for the compositor to blend.
        _collapseTimer.Start();
        _host?.OnControlsVisibilityChanged(false);
    }

    private void OnCollapseTimer()
    {
        if (_controlsVisible) return;
        Osc.Visibility = Visibility.Collapsed;
        TitleOverlay.Visibility = Visibility.Collapsed;
    }

    private void CheckAutoHide()
    {
        if (_player is null || _settings is null || Visibility != Visibility.Visible) return;
        bool keep = !_player.HasMedia || _player.IsPaused || Osc.IsInteracting || _panel != Panel.None
                    || ErrorBar.IsOpen || AudioCard.Visibility == Visibility.Visible;
        if (keep)
        {
            if (!_controlsVisible || _input.IsCursorHidden) ShowControls();
            return;
        }
        bool wasHidden = _input.IsCursorHidden;
        // Controls and mouse pointer go together (HideControls hides both).
        if ((DateTime.UtcNow - _lastActivity).TotalSeconds >= _settings.AutoHideSeconds) HideControls();
        // While hidden, keep checking that the pointer really is: menus, popups and window changes can put the
        // arrow back without the mouse moving. (Right after hiding, the surface checks by itself a moment later.)
        if (wasHidden) _input.EnsureCursorHidden();
    }

    /// <summary>Stops timers and detaches from the player before the window is destroyed.</summary>
    public void Shutdown()
    {
        _hideTimer.Stop();
        _toastTimer.Stop();
        _collapseTimer.Stop();
        _clickTimer.Stop();
        _input.Shutdown();
        QuickSettings.Shutdown();
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerChanged;
            _player.OsdMessage -= ShowToast;
        }
        if (_settings is not null) _settings.PropertyChanged -= OnSettingsChanged;
    }

    /// <summary>Shows the controls when the view is left (e.g. back to the welcome screen).</summary>
    public void ResetVisibility()
    {
        TogglePanel(Panel.None);
        ShowControls();
    }

    // ------------------------------------------------------------------ toast

    public void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(1400);
        _toastTimer.Start();
    }

    // ------------------------------------------------------------------ side panels

    public void ToggleQuickSettings() => TogglePanel(_panel == Panel.QuickSettings ? Panel.None : Panel.QuickSettings);
    public void TogglePlaylist() => TogglePanel(_panel == Panel.Playlist ? Panel.None : Panel.Playlist);
    public bool CloseSidePanel()
    {
        if (_panel == Panel.None) return false;
        TogglePanel(Panel.None);
        return true;
    }

    private void TogglePanel(Panel panel)
    {
        if (panel != Panel.None && panel == _panel) panel = Panel.None;
        _panel = panel;
        LayoutOsc(Root.ActualWidth);
        QuickSettings.Visibility = panel == Panel.QuickSettings ? Visibility.Visible : Visibility.Collapsed;
        PlaylistPanel.Visibility = panel == Panel.Playlist ? Visibility.Visible : Visibility.Collapsed;
        if (panel == Panel.None)
        {
            SidePanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (panel == Panel.QuickSettings) QuickSettings.Refresh();
        else PlaylistPanel.Refresh();

        bool wasHidden = SidePanel.Visibility != Visibility.Visible;
        SidePanel.Visibility = Visibility.Visible;
        if (wasHidden)
        {
            var anim = new DoubleAnimation
            {
                From = 40, To = 0, Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(anim, SidePanelTransform);
            Storyboard.SetTargetProperty(anim, "X");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
        ShowControls();
    }

    // ------------------------------------------------------------------ mouse on video

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // Compared on screen, not in the window: when the window changes under a still pointer (full screen
        // on/off, auto-fit, a moved window) WinUI reports a pointer move at a new position in the window, and
        // that is not the user moving the mouse. Neither are the pointer's own one-pixel reclaim moves.
        if (_input.IsReclaimingPointer || !Win32.TryGetCursorPos(out int x, out int y)) return;
        if (Math.Abs(x - _lastPointerX) + Math.Abs(y - _lastPointerY) < 3) return;
        (_lastPointerX, _lastPointerY) = (x, y);
        NotifyActivity();
    }

    private void OnInputPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(_input);
        if (!pt.Properties.IsLeftButtonPressed) return;
        _pressed = true;
        _dragged = false;
        _swallowRelease = false;
        _pressPoint = pt.Position;
        _pressTicks = Environment.TickCount64;
        Focus(FocusState.Pointer);
    }

    private void OnInputMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || _dragged || _host is null) return;
        var pt = e.GetCurrentPoint(_input);
        if (!pt.Properties.IsLeftButtonPressed) { _pressed = false; return; }
        double dx = pt.Position.X - _pressPoint.X, dy = pt.Position.Y - _pressPoint.Y;
        if (dx * dx + dy * dy < 36 || _host.IsFullscreen) return;
        _dragged = true;
        _pressed = false;
        _host.BeginWindowDrag();
    }

    private void OnInputReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        if (_dragged) return;
        if (_swallowRelease)
        {
            // Second click of a double-click: already handled as full screen.
            _swallowRelease = false;
            return;
        }
        if (_panel != Panel.None)
        {
            TogglePanel(Panel.None);
            return;
        }
        if (_settings?.ClickAction is not (ClickAction.PlayPause or ClickAction.ToggleControls)) return;
        // A double-click toggles full screen, so a single click only counts once the double-click time (measured
        // from the press, like Windows does) has passed without a second click. Acting at once would pause on the
        // first click and resume on the second: a visible stutter around every full-screen switch.
        long wait = Win32.GetDoubleClickTime() - (Environment.TickCount64 - _pressTicks);
        _clickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, wait));
        _clickTimer.Start();
    }

    private void PerformClickAction()
    {
        switch (_settings?.ClickAction)
        {
            case ClickAction.PlayPause:
                _player?.TogglePause();
                break;
            case ClickAction.ToggleControls:
                if (_controlsVisible) HideControls(); else NotifyActivity();
                break;
        }
    }

    private void OnInputCanceled(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        _swallowRelease = false;
    }

    private void OnInputDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _clickTimer.Stop(); // the first click was half of this double-click
        if (_pressed) _swallowRelease = true;
        _host?.ToggleFullscreen();
        e.Handled = true;
    }

    private void OnInputWheel(object sender, PointerRoutedEventArgs e)
    {
        if (_player is null) return;
        var pt = e.GetCurrentPoint(_input);
        int delta = pt.Properties.MouseWheelDelta;
        bool horizontal = pt.Properties.IsHorizontalMouseWheel;
        var mods = e.KeyModifiers;
        if (horizontal || mods.HasFlag(VirtualKeyModifiers.Shift))
        {
            _player.SeekRelative(5 * Math.Sign(delta), exact: false);
        }
        else if (mods.HasFlag(VirtualKeyModifiers.Control))
        {
            _player.StepSpeed(Math.Sign(delta));
        }
        else
        {
            _player.ChangeVolume(5 * Math.Sign(delta));
        }
        NotifyActivity();
        e.Handled = true;
    }

    private void OnInputRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_player is null || _host is null) return;
        var menu = ContextMenu.Build(_player, _host, this);
        menu.ShowAt(_input, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = e.GetPosition(_input) });
        e.Handled = true;
    }

    internal void SelectQuickTab(int index) => QuickSettings.SelectTab(index);
}