using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Otear.Mpv;
using Otear.Player;
using Otear.Services;
using Otear.Views;

namespace Otear.Controls;

/// <summary>IINA-style "quick settings" side panel with Video / Audio / Subtitles tabs.</summary>
public sealed partial class QuickSettingsPanel : UserControl
{
    private PlayerController? _player;
    private IPlayerHost? _host;
    private bool _syncing;
    private readonly List<long?> _videoIds = [], _audioIds = [], _subIds = [], _secondaryIds = [];
    private readonly List<string> _deviceNames = [];
    private readonly Dictionary<string, Slider> _eqSliders = [];
    private readonly Slider[] _audioEq = new Slider[10];
    private readonly DispatcherQueueTimer _audioEqTimer;

    private static readonly (string Label, string Value)[] s_aspects =
        [("Default", "no"), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9", "2.333"), ("2.39:1", "2.39"), ("1.85:1", "1.85"), ("1:1", "1:1")];
    private static readonly string[] s_eqProps = ["brightness", "contrast", "saturation", "gamma", "hue"];
    private static readonly int[] s_bands = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public event Action? CloseRequested;

    public QuickSettingsPanel()
    {
        InitializeComponent();
        _audioEqTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _audioEqTimer.Interval = TimeSpan.FromMilliseconds(200);
        _audioEqTimer.IsRepeating = false;
        _audioEqTimer.Tick += (_, _) => ApplyAudioEq();

        foreach (var (label, _) in s_aspects) AspectBox.Items.Add(label);
        BuildEqualizerRows();
        BuildAudioEq();
    }

    public void Attach(PlayerController player, AppSettings settings, IPlayerHost host)
    {
        _player = player;
        _host = host;
        player.PropertyChanged += OnPlayerChanged;
    }

    public void Shutdown()
    {
        _audioEqTimer.Stop();
        if (_player is not null) _player.PropertyChanged -= OnPlayerChanged;
    }

    /// <summary>
    /// Applies only the change that happened. (Rebuilding the whole panel on every change made slider drags
    /// redo all track lists, device lists and switches dozens of times a second.)
    /// </summary>
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible || _player is null) return;
        Measure(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerController.Tracks): RefreshTracks(); RefreshInfo(); break;
                case nameof(PlayerController.VideoId): Select(VideoTrackBox, _videoIds, _player.VideoId); RefreshInfo(); break;
                case nameof(PlayerController.AudioId): Select(AudioTrackBox, _audioIds, _player.AudioId); break;
                case nameof(PlayerController.SubtitleId): Select(SubtitleTrackBox, _subIds, _player.SubtitleId); break;
                case nameof(PlayerController.SecondarySubtitleId): Select(SecondaryTrackBox, _secondaryIds, _player.SecondarySubtitleId); break;
                case nameof(PlayerController.AudioDevices):
                case nameof(PlayerController.AudioDevice): RefreshDevices(); break;
                case nameof(PlayerController.VideoParams):
                case nameof(PlayerController.HwdecCurrent): RefreshInfo(); break;
                case nameof(PlayerController.HwdecMode): HwdecSwitch.IsOn = _player.HwdecMode != "no"; break;
                case nameof(PlayerController.Deinterlace): DeinterlaceSwitch.IsOn = _player.Deinterlace; break;
                case nameof(PlayerController.SubAssOverride): AssOverrideSwitch.IsOn = _player.SubAssOverride == "force"; break;
                case nameof(PlayerController.Speed): RefreshSpeed(); break;
                case nameof(PlayerController.SubtitleDelay): RefreshSubDelay(); break;
                case nameof(PlayerController.AudioDelay): RefreshAudioDelay(); break;
                case nameof(PlayerController.SubtitleScale): SetSlider(SubScaleSlider, _player.SubtitleScale); break;
                case nameof(PlayerController.SubtitlePosition): RefreshSubPos(); break;
                case nameof(PlayerController.VideoEqualizer): RefreshEqualizer(); break;
                case nameof(PlayerController.Rotation): RotationText.Text = $"Rotation: {_player.Rotation}°"; break;
                case nameof(PlayerController.AspectOverride): RefreshAspect(); break;
            }
        });
    }

    /// <summary>Re-reads every value from the player (when the panel is opened).</summary>
    public void Refresh()
    {
        var p = _player;
        if (p is null) return;
        Measure(() =>
        {
            RefreshTracks();
            RefreshAspect();
            RotationText.Text = $"Rotation: {p.Rotation}°";
            DeinterlaceSwitch.IsOn = p.Deinterlace;
            HwdecSwitch.IsOn = p.HwdecMode != "no";
            RefreshSpeed();
            RefreshEqualizer();
            RefreshInfo();
            RefreshDevices();
            RefreshAudioDelay();
            RefreshSubDelay();
            SetSlider(SubScaleSlider, p.SubtitleScale);
            RefreshSubPos();
            AssOverrideSwitch.IsOn = p.SubAssOverride == "force";
        });
    }

    private void Measure(Action update)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _syncing = true;
        try { update(); }
        finally
        {
            _syncing = false;
            PerfCounters.QuickRefreshes++;
            PerfCounters.QuickRefreshTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        }
    }

    private IReadOnlyList<MpvTrack>? _shownTracks;

    private void RefreshTracks()
    {
        var p = _player!;
        if (!ReferenceEquals(_shownTracks, p.Tracks))
        {
            _shownTracks = p.Tracks;
            FillTrackBox(VideoTrackBox, _videoIds, p.TracksOf(MpvTrackType.Video).Where(t => !t.IsAlbumArt), includeOff: false);
            FillTrackBox(AudioTrackBox, _audioIds, p.TracksOf(MpvTrackType.Audio), includeOff: false);
            FillTrackBox(SubtitleTrackBox, _subIds, p.TracksOf(MpvTrackType.Sub), includeOff: true);
            FillTrackBox(SecondaryTrackBox, _secondaryIds, p.TracksOf(MpvTrackType.Sub), includeOff: true);
        }
        Select(VideoTrackBox, _videoIds, p.VideoId);
        Select(AudioTrackBox, _audioIds, p.AudioId);
        Select(SubtitleTrackBox, _subIds, p.SubtitleId);
        Select(SecondaryTrackBox, _secondaryIds, p.SecondarySubtitleId);
    }

    private void RefreshAspect()
    {
        int aspect = Array.FindIndex(s_aspects, a => a.Value == _player!.AspectOverride);
        AspectBox.SelectedIndex = aspect < 0 ? 0 : aspect;
    }

    private void RefreshEqualizer()
    {
        foreach (var name in s_eqProps)
            SetSlider(_eqSliders[name], _player!.VideoEqualizer.TryGetValue(name, out var v) ? v : 0);
    }

    private void RefreshInfo() => VideoInfo.Text = DescribeVideo(_player!);

    private IReadOnlyList<(string Name, string Description)>? _shownDevices;

    private void RefreshDevices()
    {
        var p = _player!;
        if (!ReferenceEquals(_shownDevices, p.AudioDevices))
        {
            _shownDevices = p.AudioDevices;
            AudioDeviceBox.Items.Clear();
            _deviceNames.Clear();
            foreach (var (name, desc) in p.AudioDevices)
            {
                _deviceNames.Add(name);
                AudioDeviceBox.Items.Add(name == "auto" ? "System default" : desc);
            }
        }
        AudioDeviceBox.SelectedIndex = _deviceNames.IndexOf(p.AudioDevice);
    }

    private void RefreshAudioDelay()
    {
        SetSlider(AudioDelaySlider, _player!.AudioDelay);
        AudioDelayText.Text = $"Delay: {_player.AudioDelay * 1000:+0;-0;0} ms";
    }

    private void RefreshSubPos()
    {
        SetSlider(SubPosSlider, _player!.SubtitlePosition);
        SubPosText.Text = SettingsWindow.SubtitlePositionLabel((int)Math.Round(_player.SubtitlePosition));
    }

    private void RefreshSpeed()
    {
        SetSlider(SpeedSlider, _player!.Speed);
        SpeedValueText.Text = $"{_player.Speed:0.00}×";
    }

    private void RefreshSubDelay()
    {
        SetSlider(SubDelaySlider, _player!.SubtitleDelay);
        SubDelayText.Text = $"Delay: {_player.SubtitleDelay * 1000:+0;-0;0} ms";
    }

    /// <summary>Moves a slider only if the value really differs (leaves the user's drag alone).</summary>
    private static void SetSlider(Slider slider, double value)
    {
        if (Math.Abs(slider.Value - value) > 1e-6) slider.Value = value;
    }

    private static void Select(ComboBox box, List<long?> ids, long? selected)
    {
        int index = ids.IndexOf(selected);
        if (box.SelectedIndex != index) box.SelectedIndex = index;
    }

    private static void FillTrackBox(ComboBox box, List<long?> ids, IEnumerable<MpvTrack> tracks, bool includeOff)
    {
        box.Items.Clear();
        ids.Clear();
        if (includeOff) { box.Items.Add("Off"); ids.Add(null); }
        foreach (var t in tracks) { box.Items.Add(t.DisplayName); ids.Add(t.Id); }
        box.IsEnabled = ids.Count > (includeOff ? 1 : 0);
    }

    private static string DescribeVideo(PlayerController p)
    {
        var sb = new StringBuilder();
        var track = p.Tracks.FirstOrDefault(t => t.Type == MpvTrackType.Video && t.Id == p.VideoId);
        var vp = p.VideoParams;
        if (track is not null) sb.AppendLine($"Codec: {track.CodecDescription ?? track.Codec}");
        if (vp is not null)
        {
            sb.AppendLine($"Resolution: {vp.Width}×{vp.Height}" + (vp.DisplayWidth != vp.Width || vp.DisplayHeight != vp.Height ? $" (shown {vp.DisplayWidth}×{vp.DisplayHeight})" : ""));
            sb.AppendLine($"Format: {vp.HwPixelFormat ?? vp.PixelFormat}" + (vp.HwPixelFormat is not null ? $" ({vp.PixelFormat})" : ""));
            sb.AppendLine($"Dynamic range: {vp.DynamicRangeName} · {vp.Primaries} · {vp.Gamma}");
            if (vp.MaxCll is > 0) sb.AppendLine($"MaxCLL: {vp.MaxCll:0} nits");
        }
        if (track?.Fps is > 0) sb.AppendLine($"Frame rate: {track.Fps:0.###} fps");
        sb.Append($"Decoder: {(string.IsNullOrEmpty(p.HwdecCurrent) || p.HwdecCurrent == "no" ? "software" : p.HwdecCurrent)}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ layout helpers

    private void BuildEqualizerRows()
    {
        foreach (var name in s_eqProps)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name),
                Style = (Style)Resources["RowLabel"],
            });
            var slider = new Slider { Minimum = -100, Maximum = 100, StepFrequency = 1 };
            Grid.SetColumn(slider, 1);
            string prop = name;
            slider.ValueChanged += (_, e) => { if (!_syncing) _player?.SetVideoEqualizer(prop, e.NewValue); };
            row.Children.Add(slider);
            _eqSliders[name] = slider;
            EqualizerRows.Children.Add(row);
        }
    }

    private void BuildAudioEq()
    {
        AudioEqGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        AudioEqGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < s_bands.Length; i++)
        {
            AudioEqGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var slider = new Slider
            {
                Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, StepFrequency = 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ToolTipService.SetToolTip(slider, $"{BandLabel(s_bands[i])}Hz");
            slider.ValueChanged += (_, _) => { if (!_syncing) { _audioEqTimer.Stop(); _audioEqTimer.Start(); } };
            Grid.SetColumn(slider, i);
            AudioEqGrid.Children.Add(slider);
            var label = new TextBlock
            {
                Text = BandLabel(s_bands[i]), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Resources["RowLabel"],
            };
            Grid.SetColumn(label, i);
            Grid.SetRow(label, 1);
            AudioEqGrid.Children.Add(label);
            _audioEq[i] = slider;
        }
    }

    private static string BandLabel(int hz) => hz >= 1000 ? $"{hz / 1000}k" : hz.ToString(CultureInfo.InvariantCulture);

    private void ApplyAudioEq()
    {
        if (_player is null) return;
        var filters = new List<string>();
        for (int i = 0; i < s_bands.Length; i++)
        {
            double g = _audioEq[i].Value;
            if (Math.Abs(g) < 0.01) continue;
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"equalizer=f={s_bands[i]}:t=o:w=1:g={g:0.#}"));
        }
        _player.SetRaw("af", filters.Count == 0 ? "" : $"@otear-eq:lavfi=[{string.Join(',', filters)}]");
    }

    // ------------------------------------------------------------------ handlers

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        VideoPage.Visibility = sender.SelectedItem == VideoTab ? Visibility.Visible : Visibility.Collapsed;
        AudioPage.Visibility = sender.SelectedItem == AudioTab ? Visibility.Visible : Visibility.Collapsed;
        SubtitlePage.Visibility = sender.SelectedItem == SubtitleTab ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private static long? Pick(ComboBox box, List<long?> ids) =>
        box.SelectedIndex >= 0 && box.SelectedIndex < ids.Count ? ids[box.SelectedIndex] : null;

    private void OnVideoTrackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && VideoTrackBox.SelectedIndex >= 0) _player?.SelectTrack(MpvTrackType.Video, Pick(VideoTrackBox, _videoIds));
    }

    private void OnAudioTrackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && AudioTrackBox.SelectedIndex >= 0) _player?.SelectTrack(MpvTrackType.Audio, Pick(AudioTrackBox, _audioIds));
    }

    private void OnSubtitleTrackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && SubtitleTrackBox.SelectedIndex >= 0) _player?.SelectTrack(MpvTrackType.Sub, Pick(SubtitleTrackBox, _subIds));
    }

    private void OnSecondaryTrackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && SecondaryTrackBox.SelectedIndex >= 0) _player?.SelectSecondarySubtitle(Pick(SecondaryTrackBox, _secondaryIds));
    }

    private void OnAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && AspectBox.SelectedIndex >= 0) _player?.SetAspectOverride(s_aspects[AspectBox.SelectedIndex].Value);
    }

    private void OnRotateLeft(object sender, RoutedEventArgs e) => _player?.SetRotation((_player?.Rotation ?? 0) - 90);
    private void OnRotateRight(object sender, RoutedEventArgs e) => _player?.SetRotation((_player?.Rotation ?? 0) + 90);

    private void OnDeinterlaceToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _player?.SetRaw("deinterlace", DeinterlaceSwitch.IsOn ? "yes" : "no");
    }

    private void OnHwdecToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _player?.SetHardwareDecoding(HwdecSwitch.IsOn);
    }

    private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_syncing && Math.Abs(e.NewValue - (_player?.Speed ?? 1)) > 0.001) _player?.SetSpeed(e.NewValue);
    }

    private void OnResetSpeed(object sender, RoutedEventArgs e) => _player?.SetSpeed(1);

    private void OnResetEqualizer(object sender, RoutedEventArgs e)
    {
        foreach (var name in s_eqProps) _player?.SetVideoEqualizer(name, 0);
    }

    private void OnAudioDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || AudioDeviceBox.SelectedIndex < 0 || AudioDeviceBox.SelectedIndex >= _deviceNames.Count) return;
        _player?.SetAudioDevice(_deviceNames[AudioDeviceBox.SelectedIndex]);
    }

    private void OnAudioDelayChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        AudioDelayText.Text = $"Delay: {e.NewValue * 1000:+0;-0;0} ms";
        _player?.SetAudioDelay(Math.Round(e.NewValue, 3));
    }

    private void OnResetAudioDelay(object sender, RoutedEventArgs e) => _player?.SetAudioDelay(0);

    private void OnResetAudioEq(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        foreach (var s in _audioEq) s.Value = 0;
        _syncing = false;
        ApplyAudioEq();
    }

    private async void OnLoadSubtitle(object sender, RoutedEventArgs e)
    {
        if (_host is not null) await _host.OpenSubtitleAsync();
    }

    private void OnSubDelayChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        SubDelayText.Text = $"Delay: {e.NewValue * 1000:+0;-0;0} ms";
        _player?.SetSubtitleDelay(Math.Round(e.NewValue, 3));
    }

    private void OnResetSubDelay(object sender, RoutedEventArgs e) => _player?.SetSubtitleDelay(0);

    private void OnSubScaleChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_syncing) _player?.SetSubtitleScale(e.NewValue);
    }

    private void OnResetSubScale(object sender, RoutedEventArgs e) => _player?.SetSubtitleScale(1);

    private void OnSubPosChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        SubPosText.Text = SettingsWindow.SubtitlePositionLabel((int)Math.Round(e.NewValue));
        if (!_syncing) _player?.SetSubtitlePosition(e.NewValue);
    }

    private void OnResetSubPos(object sender, RoutedEventArgs e) => _player?.SetSubtitlePosition(100);

    private void OnAssOverrideToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _player?.SetRaw("sub-ass-override", AssOverrideSwitch.IsOn ? "force" : "yes");
    }

    internal void SelectTab(int index) => Tabs.SelectedItem = index switch { 1 => AudioTab, 2 => SubtitleTab, _ => VideoTab };
}