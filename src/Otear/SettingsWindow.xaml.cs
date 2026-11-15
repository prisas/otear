using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Otear.Interop;
using Otear.Mpv;
using Otear.Player;
using Otear.Services;

namespace Otear;

/// <summary>Preferences window (Windows 11 settings style).</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly PlayerController _player;
    private readonly HdrDisplayService _hdr;
    private bool _loading = true;
    private readonly List<string> _deviceNames = [];

    private static readonly (string Tag, string Label, string Glyph)[] s_pages =
    [
        ("General", "General", ""),
        ("Video", "Video", ""),
        ("Audio", "Audio", ""),
        ("Subtitles", "Subtitles", ""),
        ("Shortcuts", "Keyboard shortcuts", ""),
        ("Advanced", "Advanced", ""),
        ("About", "About", ""),
    ];

    private static readonly int[] s_volumeMax = [100, 130, 150, 200];
    private static readonly string[] s_subColors = ["#FFFFFFFF", "#FFFFE14D", "#FFD8D8D8"];

    internal SettingsWindow(AppSettings settings, PlayerController player, HdrDisplayService hdr)
    {
        _s = settings;
        _player = player;
        _hdr = hdr;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(AppPaths.Asset("Otear.ico"));
        try
        {
            var logo = new BitmapImage(new Uri(AppPaths.Asset("Logo.png")));
            TitleIcon.Source = logo;
            AboutLogo.Source = logo;
        }
        catch { }
        double scale = Win32.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(980 * scale), (int)(720 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter op) { op.PreferredMinimumWidth = (int)(560 * scale); op.PreferredMinimumHeight = (int)(400 * scale); }

        foreach (var (tag, label, glyph) in s_pages)
            Nav.MenuItems.Add(new NavigationViewItem { Content = label, Tag = tag, Icon = new FontIcon { Glyph = glyph } });

        LoadValues();
        BuildShortcuts();
        // The bar can be moved while this window is open: keep "Reset position" in step.
        _s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.ControlBarX) or nameof(AppSettings.ControlBarY)) UpdateBarPositionCard();
        };
        Nav.SelectedItem = Nav.MenuItems[0];
        _loading = false;
        AppWindow.Closing += OnClosing;
    }

    private bool _allowClose;

    /// <summary>
    /// The window is kept alive and only hidden when the user closes it, so reopening it is instant
    /// (building it takes ~150 ms); it is also pre-built once the app is idle.
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        sender.Hide();
    }

    private bool _positioned;

    /// <summary>Refreshes the values and shows the window (centred over <paramref name="owner"/> the first time).</summary>
    public void Present(AppWindow? owner = null)
    {
        if (!_positioned && owner is not null)
        {
            _positioned = true;
            var area = DisplayArea.GetFromWindowId(owner.Id, DisplayAreaFallback.Nearest).WorkArea;
            var size = AppWindow.Size;
            int x = owner.Position.X + (owner.Size.Width - size.Width) / 2;
            int y = owner.Position.Y + (owner.Size.Height - size.Height) / 2;
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - size.Width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height));
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
        _loading = true;
        try { LoadValues(); }
        finally { _loading = false; }
        AppWindow.Show();
        Activate();
    }

    /// <summary>Really closes the window (when the app exits).</summary>
    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    public void ApplyTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        var tb = AppWindow.TitleBar;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
        tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x18, 255, 255, 255) : Windows.UI.Color.FromArgb(0x12, 0, 0, 0);
    }

    private void LoadValues()
    {
        ThemeBox.SelectedIndex = (int)_s.Theme;
        BarStyleBox.SelectedIndex = (int)_s.ControlBarStyle;
        UpdateBarPositionCard();
        AutoHideBox.Value = _s.AutoHideSeconds;
        ClickBox.SelectedIndex = (int)_s.ClickAction;
        ResumeSwitch.IsOn = _s.ResumePlayback;
        AutoFitSwitch.IsOn = _s.AutoFitWindow;
        AspectSwitch.IsOn = _s.LockAspectRatio;

        HdrBox.SelectedIndex = (int)_s.HdrMode;
        HdrCard.Description = _hdr.IsHdrDisplay
            ? $"This display is in HDR mode (peak {_hdr.PeakNits:0} nits): HDR10, HLG and Dolby Vision play in HDR."
            : "This display is in SDR mode: HDR video is tone-mapped. Turn on \"Use HDR\" in Windows display settings for HDR output.";
        PeakBox.SelectedIndex = (int)_s.HdrPeakDetection;
        HwdecSwitch.IsOn = _s.HardwareDecoding;
        QualityBox.SelectedIndex = (int)_s.Quality;
        DebandSwitch.IsOn = _s.Deband;
        SmoothSwitch.IsOn = _s.SmoothMotion;
        ThumbsSwitch.IsOn = _s.SeekThumbnails;

        _deviceNames.Clear();
        DeviceBox.Items.Clear();
        var devices = _player.AudioDevices.Count > 0 ? _player.AudioDevices : [("auto", "System default")];
        foreach (var (name, desc) in devices)
        {
            _deviceNames.Add(name);
            DeviceBox.Items.Add(name == "auto" ? "System default" : desc);
        }
        DeviceBox.SelectedIndex = Math.Max(0, _deviceNames.IndexOf(_s.AudioDevice));
        ExclusiveSwitch.IsOn = _s.AudioExclusive;
        PassthroughSwitch.IsOn = _s.AudioPassthrough;
        VolumeMaxBox.SelectedIndex = Math.Max(0, Array.IndexOf(s_volumeMax, _s.VolumeMax));
        AudioLangBox.Text = _s.PreferredAudioLanguages;

        SubLangBox.Text = _s.PreferredSubtitleLanguages;
        SubAutoBox.SelectedIndex = (int)_s.SubtitleAutoLoad;
        SelectSubtitleFont();
        RefreshFonts();
        SubSizeBox.Value = _s.SubtitleFontSize;
        SubColorBox.SelectedIndex = Math.Max(0, Array.IndexOf(s_subColors, _s.SubtitleColor));
        SubBorderSlider.Value = _s.SubtitleBorderSize;
        SubPosSlider.Value = _s.SubtitlePosition;
        SubPosText.Text = SubtitlePositionLabel(_s.SubtitlePosition);

        MpvConfigSwitch.IsOn = _s.UseMpvConfigDirectory;
        ExtraOptionsBox.Text = _s.ExtraMpvOptions;
        DebugSwitch.IsOn = _s.DebugLogging;

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
        AppVersionText.Text = $"Version {version.Split('+')[0]}";
        EngineText.Text = $"{_player.MpvVersion ?? "mpv"}\nFFmpeg {_player.FfmpegVersion}\nlibplacebo {_player.LibplaceboVersion}\n" +
                          $"libmpv client API {MpvClient.ClientApiVersion >> 16}.{MpvClient.ClientApiVersion & 0xFFFF}";
    }

    private void BuildShortcuts()
    {
        (string Keys, string Action)[] rows =
        [
            ("Space  /  K", "Play / pause"),
            ("←  →", "Seek 5 seconds (Shift: 1 second)"),
            ("J  /  L", "Seek 10 seconds"),
            ("Ctrl+←  Ctrl+→", "Previous / next chapter"),
            ("↑  ↓", "Volume"),
            ("M", "Mute"),
            ("F  /  Enter  /  double-click", "Full screen"),
            ("Esc", "Exit full screen / close panel"),
            ("[  ]  /  Backspace", "Speed −/+ 0.05× / normal speed"),
            (",  .", "Previous / next frame"),
            ("0 – 9", "Jump to 0 % … 90 %"),
            ("Home", "Back to the start"),
            ("PgUp  PgDn", "Previous / next file"),
            ("A", "Next audio track"),
            ("C", "Next subtitle track"),
            ("V", "Show / hide subtitles"),
            ("Z  X", "Subtitle delay −/+ 100 ms"),
            ("S  /  Shift+S", "Screenshot with / without subtitles"),
            ("I", "Playback statistics"),
            ("P", "Playlist & chapters panel"),
            ("Q", "Video, audio & subtitle panel"),
            ("Ctrl+L", "Loop current file"),
            ("Ctrl+O  /  Ctrl+Shift+O", "Open file / folder"),
            ("Ctrl+U", "Open URL"),
            ("Ctrl+W", "Close file"),
            ("Ctrl+,", "Settings"),
        ];
        ShortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        ShortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Length; i++)
        {
            ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = rows[i].Keys, FontFamily = new FontFamily("Consolas"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var a = new TextBlock { Text = rows[i].Action, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(k, i);
            Grid.SetRow(a, i);
            Grid.SetColumn(a, 1);
            ShortcutGrid.Children.Add(k);
            ShortcutGrid.Children.Add(a);
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "General";
        PageTitle.Text = s_pages.First(p => p.Tag == tag).Label;
        GeneralPage.Visibility = Vis(tag == "General");
        VideoPage.Visibility = Vis(tag == "Video");
        AudioPage.Visibility = Vis(tag == "Audio");
        SubtitlePage.Visibility = Vis(tag == "Subtitles");
        ShortcutsPage.Visibility = Vis(tag == "Shortcuts");
        AdvancedPage.Visibility = Vis(tag == "Advanced");
        AboutPage.Visibility = Vis(tag == "About");
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------------ general

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) _s.Theme = (AppTheme)ThemeBox.SelectedIndex; }
    private void OnBarStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) _s.ControlBarStyle = (ControlBarStyle)BarStyleBox.SelectedIndex;
        UpdateBarPositionCard();
    }

    private void OnResetBarPosition(object sender, RoutedEventArgs e)
    {
        _s.ResetControlBarPosition();
        UpdateBarPositionCard();
    }

    private void UpdateBarPositionCard()
    {
        // Only the floating bar moves; the bottom bar is docked.
        BarPositionCard.IsEnabled = BarStyleBox.SelectedIndex != (int)ControlBarStyle.Bottom;
        ResetBarPositionButton.IsEnabled = _s.HasCustomControlBarPosition;
    }
    private void OnClickActionChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) _s.ClickAction = (ClickAction)ClickBox.SelectedIndex; }
    private void OnResumeToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.ResumePlayback = ResumeSwitch.IsOn;
        _player.SetRaw("resume-playback", ResumeSwitch.IsOn ? "yes" : "no");
    }
    private void OnAutoFitToggled(object sender, RoutedEventArgs e) { if (!_loading) _s.AutoFitWindow = AutoFitSwitch.IsOn; }
    private void OnAspectToggled(object sender, RoutedEventArgs e) { if (!_loading) _s.LockAspectRatio = AspectSwitch.IsOn; }

    private void OnAutoHideChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loading && !double.IsNaN(args.NewValue)) _s.AutoHideSeconds = args.NewValue;
    }

    private async void OnOpenDefaultApps(object sender, RoutedEventArgs e)
    {
        // The installer registers per user by default, or for all users when installed as admin.
        bool machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\RegisteredApplications")?.GetValue("Otear") is not null;
        bool user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications")?.GetValue("Otear") is not null;
        string uri = user ? "ms-settings:defaultapps?registeredAppUser=Otear"
                   : machine ? "ms-settings:defaultapps?registeredAppMachine=Otear"
                   : "ms-settings:defaultapps";
        await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }

    // ------------------------------------------------------------------ video

    private void OnHdrChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) _s.HdrMode = (HdrMode)HdrBox.SelectedIndex; }

    private void OnPeakChanged(object sender, SelectionChangedEventArgs e) { if (!_loading && PeakBox.SelectedIndex >= 0) _s.HdrPeakDetection = (PeakDetectionMode)PeakBox.SelectedIndex; }

    private void OnHwdecToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.HardwareDecoding = HwdecSwitch.IsOn;
        _player.SetHardwareDecoding(HwdecSwitch.IsOn);
    }

    private void OnQualityChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) _s.Quality = (VideoQuality)QualityBox.SelectedIndex; }

    private void OnDebandToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Deband = DebandSwitch.IsOn;
        _player.SetRaw("deband", DebandSwitch.IsOn ? "yes" : "no");
    }

    private void OnSmoothToggled(object sender, RoutedEventArgs e) { if (!_loading) _s.SmoothMotion = SmoothSwitch.IsOn; }
    private void OnThumbsToggled(object sender, RoutedEventArgs e) { if (!_loading) _s.SeekThumbnails = ThumbsSwitch.IsOn; }

    // ------------------------------------------------------------------ audio

    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DeviceBox.SelectedIndex < 0 || DeviceBox.SelectedIndex >= _deviceNames.Count) return;
        _player.SetAudioDevice(_deviceNames[DeviceBox.SelectedIndex]);
    }

    private void OnExclusiveToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AudioExclusive = ExclusiveSwitch.IsOn;
        _player.SetRaw("audio-exclusive", ExclusiveSwitch.IsOn ? "yes" : "no");
    }

    private void OnPassthroughToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.AudioPassthrough = PassthroughSwitch.IsOn;
        _player.SetRaw("audio-spdif", PassthroughSwitch.IsOn ? "ac3,eac3,dts,dts-hd,truehd" : "");
    }

    private void OnVolumeMaxChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || VolumeMaxBox.SelectedIndex < 0) return;
        _s.VolumeMax = s_volumeMax[VolumeMaxBox.SelectedIndex];
        _player.SetRaw("volume-max", _s.VolumeMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnAudioLangChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.PreferredAudioLanguages = AudioLangBox.Text.Trim();
        _player.SetRaw("alang", _s.PreferredAudioLanguages);
    }

    // ------------------------------------------------------------------ subtitles

    private void OnSubLangChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.PreferredSubtitleLanguages = SubLangBox.Text.Trim();
        _player.SetRaw("slang", _s.PreferredSubtitleLanguages);
    }

    private void OnSubAutoChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SubAutoBox.SelectedIndex < 0) return;
        _s.SubtitleAutoLoad = (SubtitleAutoLoad)SubAutoBox.SelectedIndex;
        _player.SetRaw("sub-auto", _s.SubtitleAutoLoad switch
        {
            SubtitleAutoLoad.Exact => "exact",
            SubtitleAutoLoad.All => "all",
            SubtitleAutoLoad.None => "no",
            _ => "fuzzy",
        });
    }

    private IReadOnlyList<SystemFontFamily>? _fonts;
    private bool _fontsLoading, _fillingFonts;

    /// <summary>
    /// Lists the installed fonts in the background (the window opens without waiting) and refreshes the list
    /// each time the window is shown, so fonts installed meanwhile appear.
    /// </summary>
    private async void RefreshFonts()
    {
        if (_fontsLoading) return;
        _fontsLoading = true;
        try
        {
            var fonts = await Task.Run(DirectWrite.GetSystemFontFamilies);
            if (_fonts is not null && _fonts.SequenceEqual(fonts)) return;
            _fonts = fonts;
            FillFontChoices();
        }
        catch (Exception ex)
        {
            Log.Warn($"Listing installed fonts failed: {ex.Message}");
            if (_fonts is null)
            {
                _fonts = [];
                FillFontChoices();
            }
        }
        finally
        {
            _fontsLoading = false;
        }
    }

    private void FillFontChoices()
    {
        if (_fonts is null) return;
        var choices = new List<FontChoice> { new(string.Empty, "Default (sans-serif)", ownFont: false) };
        string saved = _s.SubtitleFont;
        // Keep a font chosen earlier that is no longer installed (mpv falls back to the default for it).
        if (saved.Length > 0 && !_fonts.Any(f => string.Equals(f.Name, saved, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new FontChoice(saved, $"{saved} (not installed)", ownFont: false));
        choices.AddRange(_fonts.Select(f => new FontChoice(f.Name, f.Name, ownFont: !f.IsSymbol)));
        _fillingFonts = true;
        try { SubFontBox.ItemsSource = choices; }
        finally { _fillingFonts = false; }
        SelectSubtitleFont();
    }

    private void SelectSubtitleFont()
    {
        if (SubFontBox.ItemsSource is not List<FontChoice> choices) return;
        var match = choices.Find(c => string.Equals(c.Value, _s.SubtitleFont, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            FillFontChoices(); // the saved font changed to one not in the list: it gets its own entry
            return;
        }
        _fillingFonts = true;
        try { SubFontBox.SelectedItem = match; }
        finally { _fillingFonts = false; }
    }

    private void OnSubFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _fillingFonts || SubFontBox.SelectedItem is not FontChoice choice) return;
        if (string.Equals(choice.Value, _s.SubtitleFont, StringComparison.Ordinal)) return;
        _s.SubtitleFont = choice.Value;
        _player.SetRaw("sub-font", choice.Value.Length == 0 ? "sans-serif" : choice.Value);
    }

    private void OnSubSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        _s.SubtitleFontSize = (int)args.NewValue;
        _player.SetRaw("sub-font-size", _s.SubtitleFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void OnSubColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SubColorBox.SelectedIndex < 0) return;
        _s.SubtitleColor = s_subColors[SubColorBox.SelectedIndex];
        _player.SetRaw("sub-color", _s.SubtitleColor);
    }

    private void OnSubBorderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        _s.SubtitleBorderSize = e.NewValue;
        _player.SetRaw("sub-border-size", MpvClient.Format(e.NewValue));
    }

    private void OnSubPosChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        SubPosText.Text = SubtitlePositionLabel((int)Math.Round(e.NewValue));
        if (_loading) return;
        _player.SetSubtitlePosition(e.NewValue); // applies now and saves it
    }

    internal static string SubtitlePositionLabel(int position) => position >= 100 ? "Default" : $"{100 - position} % up";

    // ------------------------------------------------------------------ advanced

    private void OnMpvConfigToggled(object sender, RoutedEventArgs e) { if (!_loading) _s.UseMpvConfigDirectory = MpvConfigSwitch.IsOn; }
    private void OnExtraOptionsChanged(object sender, RoutedEventArgs e) { if (!_loading) _s.ExtraMpvOptions = ExtraOptionsBox.Text; }
    private void OnDebugToggled(object sender, RoutedEventArgs e) { if (!_loading) _s.DebugLogging = DebugSwitch.IsOn; }

    private async void OnOpenMpvFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.MpvConfig);
        await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.MpvConfig);
    }

    private async void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.Logs);
    }

#if DEBUG
    internal void TourSelectPage(int index) => Nav.SelectedItem = Nav.MenuItems[index];
#endif
}

/// <summary>One entry of the subtitle font list; <see cref="Value"/> is what is saved and passed to mpv.</summary>
public sealed partial class FontChoice(string value, string display, bool ownFont)
{
    private FontFamily? _preview;

    public string Value { get; } = value;
    public string Display { get; } = display;

    /// <summary>The name is drawn in its own font, except symbol/icon fonts whose glyphs would be unreadable.</summary>
    public FontFamily Preview => _preview ??= ownFont ? new FontFamily(Value) : FontFamily.XamlAutoFontFamily;

    // Used by the ComboBox's type-to-search and by screen readers.
    public override string ToString() => Display;
}