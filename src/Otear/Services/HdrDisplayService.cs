using Microsoft.Graphics.Display;
using Microsoft.UI.Windowing;
using Otear.Player;

namespace Otear.Services;

/// <summary>
/// Tracks whether the monitor showing the window is in Windows HDR mode (and its peak brightness)
/// and tells mpv how to output color. mpv can't detect this itself in composition mode.
/// </summary>
internal sealed class HdrDisplayService : IDisposable
{
    private readonly DisplayInformation _display;
    private readonly PlayerController _player;
    private readonly AppSettings _settings;

    public bool IsHdrDisplay { get; private set; }
    public double PeakNits { get; private set; }
    public double SdrWhiteNits { get; private set; }

    public HdrDisplayService(AppWindow window, PlayerController player, AppSettings settings)
    {
        _player = player;
        _settings = settings;
        _display = DisplayInformation.CreateForWindowId(window.Id);
        _display.AdvancedColorInfoChanged += OnChanged;
        _settings.PropertyChanged += OnSettingChanged;
        Apply();
    }

    private void OnChanged(DisplayInformation sender, object args) => Apply();

    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.HdrMode)) Apply();
    }

    private void Apply()
    {
        try
        {
            var info = _display.GetAdvancedColorInfo();
            IsHdrDisplay = info.CurrentAdvancedColorKind == DisplayAdvancedColorKind.HighDynamicRange;
            PeakNits = info.MaxLuminanceInNits;
            SdrWhiteNits = info.SdrWhiteLevelInNits;
            Log.Info($"Display: {info.CurrentAdvancedColorKind}, peak {PeakNits:0} nits, SDR white {SdrWhiteNits:0} nits");
        }
        catch (Exception ex)
        {
            Log.Error("Could not read display color info", ex);
            IsHdrDisplay = false;
        }
        _ = _player.ApplyDisplayColorAsync(IsHdrDisplay && _settings.HdrMode == HdrMode.Auto, PeakNits);
    }

    public void Dispose()
    {
        _display.AdvancedColorInfoChanged -= OnChanged;
        _settings.PropertyChanged -= OnSettingChanged;
        _display.Dispose();
    }
}
