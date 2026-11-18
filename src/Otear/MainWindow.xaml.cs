using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using Otear.Interop;
using Otear.Mpv;
using Otear.Player;
using Otear.Services;
using Otear.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Otear;

public sealed partial class MainWindow : Window, IPlayerHost
{
    private SettingsService _settingsService = null!;
    private RecentFilesService _recent = null!;
    private PlayerController _player = null!;
    private HdrDisplayService _hdr = null!;
    private WindowSizing _sizing = null!;
    private ThumbnailService _thumbnails = null!;
    private OverlappedPresenter _overlapped = null!;
    private readonly nint _hwnd;
    private SettingsWindow? _settingsWindow;
    private bool _playerMode;
    private bool _controlsVisible = true;
    private bool _autoFitPending;
    private string? _lastPath;
    private double _lastPosition, _lastDuration;
    private bool _closed;

    private AppSettings Settings => _settingsService.Settings;

    /// <summary>
    /// Builds only the window and its XAML. It is created first thing at launch: creating the window starts the
    /// compositor's GPU device creation (≈140 ms of driver initialisation on the first device in a process),
    /// so the earlier it starts, the earlier the first frame. Everything else happens in <see cref="Initialize"/>.
    /// </summary>
    internal MainWindow()
    {
        StartupTrace.Mark("windowCtor");
        InitializeComponent();
        StartupTrace.Mark("xaml");
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = "Otear";
    }

    internal void Initialize(SettingsService settings, PlayerController player, IReadOnlyList<string> startupItems)
    {
        _settingsService = settings;
        _player = player;
        _recent = new RecentFilesService();

        // 1) Content and services first. The first title-bar call below blocks ~100-150 ms while the Windows
        //    App SDK sets up its title-bar service; doing this work before it lets the two overlap.
        Video.WindowHandle = _hwnd;
        Video.Attach(_player);
        Player.Attach(_player, Settings, this);
        Player.AttachInputSource(Video);
        Welcome.Attach(this, _recent);
        Welcome.RecentRequested += path => _ = OpenItemsAsync([path], append: false);
        _player.PropertyChanged += OnPlayerChanged;
        _player.VideoReconfigured += OnVideoReconfigured;
        _player.FileStarted += OnFileStarted;
        _player.FileLoaded += OnFileLoaded;
        Settings.PropertyChanged += OnSettingsChanged;
        _thumbnails = new ThumbnailService(_player, Settings);
        Player.ControlBar.SeekBar.ThumbnailProvider = _thumbnails.Get;
        Player.ControlBar.SeekBar.ThumbnailWidth = 200;
        _sizing = new WindowSizing(_hwnd) { Enabled = Settings.LockAspectRatio, ContentSize = ContentPixelSize };
        Closed += OnClosed;
        try { TitleBarIcon.Source = new BitmapImage(new Uri(AppPaths.Asset("Logo.png"))); } catch { }
        _overlapped = OverlappedPresenter.Create();
        _overlapped.PreferredMinimumWidth = 480;
        _overlapped.PreferredMinimumHeight = 300;
        AppWindow.SetPresenter(_overlapped);
        SizeAndCenter(1120, 700);
        _hdr = new HdrDisplayService(AppWindow, _player, Settings);
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;
        _suppressCaptionUpdates = true;
        ApplyTheme();
        SetPlayerMode(false);
        _suppressCaptionUpdates = false;
        StartupTrace.Mark("content");
        _startupItems = startupItems;
        _startupWithFile = startupItems.Count > 0;
    }

    /// <summary>
    /// Sets up the title bar and shows the window. The first title-bar call waits for the compositor's GPU
    /// device (created in the background since the window was constructed), so all other setup happens before
    /// it and only the minimum after it.
    /// </summary>
    internal void Show()
    {
        AppWindow.SetIcon(AppPaths.Asset("Otear.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        StartupTrace.Mark("titleBar");
        UpdateCaptionButtons();
        SystemBackdrop = new MicaBackdrop();
        StartupTrace.Mark("chrome");

        // Command-line files: opened here, once the GPU driver is initialised and before the first frame.
        // Opening earlier makes mpv's GPU setup compete with the window's; opening after the first frame
        // starts playback later. Both were measured slower (tools/Measure-Startup.ps1).
        if (_startupWithFile) _ = _player.OpenAsync(_startupItems);
        StartupTrace.Mark("windowReady");
        Activate();
        StartupTrace.Mark("activated");
        WatchFirstFrame();
        if (Environment.GetEnvironmentVariable("OTEAR_BENCH_UI") is { Length: > 0 } uiBenchOut)
            _ = RunUiBenchmarkAsync(uiBenchOut);
        if (Environment.GetEnvironmentVariable("OTEAR_BENCH_SECONDS") is { Length: > 0 } benchSeconds
            && double.TryParse(benchSeconds, System.Globalization.CultureInfo.InvariantCulture, out var benchS)
            && Environment.GetEnvironmentVariable("OTEAR_BENCH_OUT") is { Length: > 0 } benchOut)
            _ = RunBenchmarkAsync(benchS, benchOut);
#if DEBUG
        if (Environment.GetEnvironmentVariable("OTEAR_TOUR_DIR") is { Length: > 0 } tourDir
            && Environment.GetEnvironmentVariable("OTEAR_TOUR_MEDIA") is { Length: > 0 } tourMedia)
            _ = RunTourAsync(tourDir, tourMedia);
#endif
    }

    private double Scale => Win32.GetDpiForWindow(_hwnd) / 96.0;

    /// <summary>Size of the XAML content (= video area) in physical pixels.</summary>
    private (int Width, int Height) ContentPixelSize()
    {
        double s = Root.XamlRoot?.RasterizationScale ?? Scale;
        return ((int)Math.Round(Root.ActualWidth * s), (int)Math.Round(Root.ActualHeight * s));
    }

    private void SizeAndCenter(double widthDip, double heightDip)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(widthDip * Scale), area.Width);
        int h = Math.Min((int)(heightDip * Scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    // ------------------------------------------------------------------ modes, theme, caption buttons

    private void SetPlayerMode(bool player)
    {
        _playerMode = player;
        Player.Visibility = player ? Visibility.Visible : Visibility.Collapsed;
        Welcome.Visibility = player ? Visibility.Collapsed : Visibility.Visible;
        TitleBarContent.Visibility = player ? Visibility.Collapsed : Visibility.Visible;
        Video.IsActive = player;
        if (player) Player.Focus(FocusState.Programmatic);
        if (!player)
        {
            Player.ResetVisibility();
            _sizing.AspectRatio = 0;
            _controlsVisible = true;
            Title = "Otear";
            if (IsFullscreen) ToggleFullscreen();
        }
        UpdateCaptionButtons();
        UpdateDragRegion();
    }

    private void ApplyTheme()
    {
        Root.RequestedTheme = Settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        _settingsWindow?.ApplyTheme(Root.RequestedTheme);
        UpdateCaptionButtons();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateCaptionButtons();

    public void OnControlsVisibilityChanged(bool visible)
    {
        _controlsVisible = visible;
        UpdateCaptionButtons();
        UpdateDragRegion();
    }

    /// <summary>
    /// The title-bar strip is a drag region, where Windows draws the pointer: while the player's controls are
    /// hidden (and in full screen) it is video like the rest, so the pointer hides there too. Moving the mouse
    /// brings the controls, and the drag region, back.
    /// </summary>
    private void UpdateDragRegion()
    {
        var visibility = !IsFullscreen && (!_playerMode || _controlsVisible) ? Visibility.Visible : Visibility.Collapsed;
        if (TitleBarArea.Visibility != visibility) TitleBarArea.Visibility = visibility;
    }

    private (bool Player, bool Visible, bool Dark)? _captionState;
    private bool _suppressCaptionUpdates;

    private void UpdateCaptionButtons()
    {
        if (_suppressCaptionUpdates) return;
        // Each title-bar colour change is a round trip to the windowing service: skip it when nothing changed.
        var state = (_playerMode, _controlsVisible, Root.ActualTheme == ElementTheme.Dark);
        if (_captionState == state) return;
        _captionState = state;
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        if (_playerMode)
        {
            tb.ButtonForegroundColor = _controlsVisible ? Colors.White : Colors.Transparent;
            tb.ButtonInactiveForegroundColor = _controlsVisible ? Color.FromArgb(0x99, 255, 255, 255) : Colors.Transparent;
            tb.ButtonHoverForegroundColor = Colors.White;
            tb.ButtonHoverBackgroundColor = Color.FromArgb(0x33, 255, 255, 255);
            tb.ButtonPressedForegroundColor = Colors.White;
            tb.ButtonPressedBackgroundColor = Color.FromArgb(0x22, 255, 255, 255);
        }
        else
        {
            bool dark = Root.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Colors.White : Colors.Black;
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0x80, 255, 255, 255) : Color.FromArgb(0x80, 0, 0, 0);
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x18, 255, 255, 255) : Color.FromArgb(0x12, 0, 0, 0);
            tb.ButtonPressedForegroundColor = fg;
            tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x10, 255, 255, 255) : Color.FromArgb(0x0C, 0, 0, 0);
        }
    }

    // ------------------------------------------------------------------ player events

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerController.HasMedia):
                if (!_player.HasMedia) RememberPosition();
                SetPlayerMode(_player.HasMedia);
                break;
            case nameof(PlayerController.MediaTitle):
                Title = string.IsNullOrEmpty(_player.MediaTitle) ? "Otear" : $"{_player.MediaTitle} – Otear";
                break;
            case nameof(PlayerController.Position):
                if (_player.Path is not null) { _lastPosition = _player.Position; _lastDuration = _player.Duration; }
                break;
        }
    }

    private void OnFileStarted()
    {
        RememberPosition();
        _autoFitPending = true;
    }

    private void OnFileLoaded()
    {
        var path = _player.Path;
        if (string.IsNullOrEmpty(path)) return;
        _lastPath = path;
        _recent.Touch(path, _player.MediaTitle, _player.Position, _player.Duration);
    }

    private void RememberPosition()
    {
        if (_lastPath is null) return;
        _recent.UpdatePosition(_lastPath, _lastPosition, _lastDuration);
        _lastPath = null;
    }

    private void OnVideoReconfigured(MpvVideoParams? vp)
    {
        if (vp is null || vp.DisplayWidth <= 0 || vp.DisplayHeight <= 0) return;
        bool rotated = (vp.Rotate % 180) != 0;
        double w = rotated ? vp.DisplayHeight : vp.DisplayWidth;
        double h = rotated ? vp.DisplayWidth : vp.DisplayHeight;
        _sizing.AspectRatio = w / h;
        if (_autoFitPending && Settings.AutoFitWindow && !IsFullscreen && _overlapped.State == OverlappedPresenterState.Restored)
            FitWindowToVideo(w, h);
        _autoFitPending = false;
    }

    /// <summary>Resizes the window to the video's size (≤ 85 % of the screen), keeping its centre.</summary>
    private void FitWindowToVideo(double videoW, double videoH)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double maxW = area.Width * 0.85, maxH = area.Height * 0.85;
        double s = Math.Min(1, Math.Min(maxW / videoW, maxH / videoH));
        double w = videoW * s, h = videoH * s;
        double minW = 640 * Scale;
        if (w < minW)
        {
            h *= minW / w;
            w = minW;
            if (h > maxH) { w *= maxH / h; h = maxH; }
        }
        var oldPos = AppWindow.Position;
        var oldSize = AppWindow.Size;
        double cx = oldPos.X + oldSize.Width / 2.0, cy = oldPos.Y + oldSize.Height / 2.0;
        // With the content extended into the title bar, the video area is the whole XAML root, which is
        // larger than AppWindow.ClientSize; size the outer window from the measured frame instead.
        var (contentW, contentH) = ContentPixelSize();
        int frameW = Math.Max(0, oldSize.Width - contentW), frameH = Math.Max(0, oldSize.Height - contentH);
        AppWindow.Resize(new SizeInt32((int)Math.Round(w) + frameW, (int)Math.Round(h) + frameH));
        var size = AppWindow.Size;
        int x = (int)Math.Clamp(cx - size.Width / 2.0, area.X, Math.Max(area.X, area.X + area.Width - size.Width));
        int y = (int)Math.Clamp(cy - size.Height / 2.0, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height));
        AppWindow.Move(new PointInt32(x, y));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Theme): ApplyTheme(); break;
            case nameof(AppSettings.LockAspectRatio): _sizing.Enabled = Settings.LockAspectRatio && !IsFullscreen; break;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange) return;
        bool fs = IsFullscreen;
        Player.ControlBar.SetFullscreen(fs);
        UpdateDragRegion();
        _sizing.Enabled = !fs && Settings.LockAspectRatio;
    }

    // ------------------------------------------------------------------ IPlayerHost

    public bool IsFullscreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public bool IsAlwaysOnTop
    {
        get => _overlapped.IsAlwaysOnTop;
        set => _overlapped.IsAlwaysOnTop = value;
    }

    public void ToggleFullscreen()
    {
        if (IsFullscreen) AppWindow.SetPresenter(_overlapped);
        else if (_playerMode) AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    public void BeginWindowDrag()
    {
        Win32.ReleaseCapture();
        Win32.SendMessage(_hwnd, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, 0);
    }

    public async Task OpenFilesAsync(bool append = false)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add("*");
            var results = await picker.PickMultipleFilesAsync();
            var paths = results?.Select(r => r.Path).Where(p => !string.IsNullOrEmpty(p)).ToList() ?? [];
            if (paths.Count > 0) await OpenItemsAsync(paths, append);
        }
        catch (Exception ex)
        {
            Log.Error("File picker failed", ex);
        }
    }

    public async Task OpenFolderAsync()
    {
        try
        {
            var picker = new FolderPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.VideosLibrary };
            var result = await picker.PickSingleFolderAsync();
            if (result?.Path is { Length: > 0 } path) await OpenItemsAsync([path], append: false);
        }
        catch (Exception ex)
        {
            Log.Error("Folder picker failed", ex);
        }
    }

    public async Task OpenSubtitleAsync()
    {
        if (!_player.HasMedia) return;
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id) { ViewMode = PickerViewMode.List };
            foreach (var ext in MediaFiles.Subtitle) picker.FileTypeFilter.Add(ext);
            var result = await picker.PickSingleFileAsync();
            if (result?.Path is { Length: > 0 } path) await _player.AddSubtitleAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error("Subtitle picker failed", ex);
        }
    }

    public async Task OpenUrlAsync()
    {
        var box = new TextBox { PlaceholderText = "https://…", Width = 420 };
        var dialog = new ContentDialog
        {
            Title = "Open URL",
            Content = box,
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { dialog.Hide(); _ = OpenItemsAsync([box.Text.Trim()], append: false); }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
            await OpenItemsAsync([box.Text.Trim()], append: false);
    }

    public void ShowSettings() => EnsureSettingsWindow().Present(AppWindow);

    private SettingsWindow EnsureSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(Settings, _player, _hdr);
            _settingsWindow.ApplyTheme(Root.RequestedTheme);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        return _settingsWindow;
    }

    /// <summary>Builds the (hidden) settings window when the app is idle, so the first open is instant too.</summary>
    private async void PrewarmSettingsWindow()
    {
        await Task.Delay(3000);
        if (_closed) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_closed) return;
            try { EnsureSettingsWindow(); }
            catch (Exception ex) { Log.Warn($"Settings pre-build failed: {ex.Message}"); }
        });
    }

    public void CloseMedia()
    {
        RememberPosition();
        _player.Stop();
    }

    /// <summary>Opens files, folders or URLs (folders expand to their media files).</summary>
    public async Task OpenItemsAsync(IReadOnlyList<string> items, bool append)
    {
        var expanded = MediaFiles.Expand(items.Where(i => !string.IsNullOrWhiteSpace(i)));
        if (expanded.Count == 0) return;
        if (!append) RememberPosition();
        await _player.OpenAsync(expanded, append);
    }

    /// <summary>A second launch (e.g. double-clicking a file in Explorer) was redirected here.</summary>
    internal void HandleExternalActivation(IReadOnlyList<string> items)
    {
        Log.Info("handoff received");
        if (items.Count > 0)
        {
            void OnPlaying()
            {
                _player.PlaybackRestarted -= OnPlaying;
                Log.Info("handoff playing");
            }
            _player.PlaybackRestarted += OnPlaying;
        }
        if (Win32.IsIconic(_hwnd)) Win32.ShowWindow(_hwnd, Win32.SW_RESTORE);
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        if (items.Count > 0) _ = OpenItemsAsync(items, append: false);
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Link;
        bool append = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift);
        e.DragUIOverride.Caption = append ? "Add to playlist" : "Play";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        bool append = e.Modifiers.HasFlag(Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift);
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (_player.HasMedia && paths.Count > 0 && paths.All(MediaFiles.IsSubtitle))
            {
                foreach (var p in paths) await _player.AddSubtitleAsync(p);
                return;
            }
            await OpenItemsAsync(paths, append && _player.HasMedia);
        }
        catch (Exception ex)
        {
            Log.Error("Drop failed", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ------------------------------------------------------------------ keyboard

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        if (focused is TextBox or PasswordBox or AutoSuggestBox or ComboBox or NumberBox) return;
        // Keys typed in a flyout, menu or dialog belong to it (e.g. arrows on the speed slider, menu navigation).
        if (focused is not null && !IsInMainContent(focused)) return;
        // A focused slider keeps its own keys: arrows step it, Home/End/Page jump.
        if (focused is Slider && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
                or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown) return;
        bool ctrl = IsDown(VirtualKey.Control), shift = IsDown(VirtualKey.Shift);
        if (HandleKey(e.Key, ctrl, shift))
        {
            e.Handled = true;
            if (_playerMode) Player.NotifyActivity(showPointer: false);
        }
    }

    /// <summary>
    /// False for elements in popups (flyouts, menus, dialogs): their visual tree does not reach Root. The window's
    /// own elements above Root count as main content: clicking the video (which cannot take focus) or closing a
    /// menu puts the focus on the window's root ScrollViewer, and the shortcuts must keep working then.
    /// </summary>
    private bool IsInMainContent(DependencyObject element)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, Root)) return true;
        for (var d = VisualTreeHelper.GetParent(Root); d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, element)) return true;
        return false;
    }

    private bool HandleKey(VirtualKey key, bool ctrl, bool shift)
    {
        const VirtualKey Comma = (VirtualKey)188, Period = (VirtualKey)190, BracketLeft = (VirtualKey)219, BracketRight = (VirtualKey)221;

        if (ctrl)
        {
            switch (key)
            {
                case VirtualKey.O when shift: _ = OpenFolderAsync(); return true;
                case VirtualKey.O: _ = OpenFilesAsync(); return true;
                case VirtualKey.U: _ = OpenUrlAsync(); return true;
                case Comma: ShowSettings(); return true;
                case VirtualKey.W: CloseMedia(); return true;
                case VirtualKey.L when _player.HasMedia: _player.SetLoopFile(_player.LoopFile == "no"); return true;
                case VirtualKey.Left when _player.HasMedia: _player.PreviousChapter(); return true;
                case VirtualKey.Right when _player.HasMedia: _player.NextChapter(); return true;
            }
            return false;
        }

        if (key == VirtualKey.Escape)
        {
            if (Player.CloseSidePanel()) return true;
            if (IsFullscreen) { ToggleFullscreen(); return true; }
            return false;
        }
        if (!_player.HasMedia) return false;

        switch (key)
        {
            case VirtualKey.Space:
            case VirtualKey.K:
                _player.TogglePause(); return true;
            case VirtualKey.Left: _player.SeekRelative(shift ? -1 : -5); return true;
            case VirtualKey.Right: _player.SeekRelative(shift ? 1 : 5); return true;
            case VirtualKey.J: _player.SeekRelative(-10); return true;
            case VirtualKey.L: _player.SeekRelative(10); return true;
            case VirtualKey.Up: _player.ChangeVolume(5); return true;
            case VirtualKey.Down: _player.ChangeVolume(-5); return true;
            case VirtualKey.M: _player.ToggleMute(); return true;
            case VirtualKey.F:
            case VirtualKey.Enter:
                ToggleFullscreen(); return true;
            case BracketLeft: _player.StepSpeed(-1); return true;
            case BracketRight: _player.StepSpeed(1); return true;
            case VirtualKey.Back: _player.SetSpeed(1); return true;
            case Comma: _player.FrameStep(backwards: true); return true;
            case Period: _player.FrameStep(backwards: false); return true;
            case VirtualKey.V: _player.ToggleSubtitleVisibility(); return true;
            case VirtualKey.C: _player.CycleSubtitles(); return true;
            case VirtualKey.A: _player.CycleAudio(); return true;
            case VirtualKey.Z: _player.AdjustSubtitleDelay(-0.1); return true;
            case VirtualKey.X: _player.AdjustSubtitleDelay(0.1); return true;
            case VirtualKey.PageUp: _player.PlaylistPrevious(); return true;
            case VirtualKey.PageDown: _player.PlaylistNext(); return true;
            case VirtualKey.Home: _player.SeekTo(0, exact: true); return true;
            case VirtualKey.S: _ = _player.ScreenshotAsync(withSubtitles: !shift); return true;
            case VirtualKey.I: _player.ToggleStats(); return true;
            case VirtualKey.P: Player.TogglePlaylist(); return true;
            case VirtualKey.Q: Player.ToggleQuickSettings(); return true;
            case >= VirtualKey.Number0 and <= VirtualKey.Number9 when _player.Duration > 0:
                _player.SeekTo(_player.Duration * (key - VirtualKey.Number0) / 10.0, exact: false); return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ shutdown

    /// <summary>
    /// Close requested: hide the window at once so closing feels instant, and give up the single-instance
    /// key so a launch during the remaining cleanup starts a fresh instance instead of being sent here.
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        Log.Info("close requested");
        try { Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().UnregisterKey(); }
        catch (Exception ex) { Log.Warn($"UnregisterKey failed: {ex.Message}"); }
        // Stop playback sound immediately; the rest of the teardown happens unseen.
        _player.Pause();
        sender.Hide();
        Log.Info("window hidden");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var steps = new System.Text.StringBuilder();
        void Step(string name) { steps.Append(System.Globalization.CultureInfo.InvariantCulture, $" {name}={sw.Elapsed.TotalMilliseconds:0}"); }
        Log.Info("shutdown begin");
        Player.Shutdown();
        RememberPosition();
        Step("recent");
        _settingsWindow?.CloseForReal();
        AppWindow.Changed -= OnAppWindowChanged;
        _hdr.Dispose();
        Step("hdr");
        _thumbnails.Dispose();
        Step("thumbnails");
        _sizing.Dispose();
        _player.Dispose();
        Step("player");
        _settingsService.Dispose();
        Step("settings");
        Log.Info("shutdown:" + steps);
    }
}
