namespace Otear.Views;

/// <summary>What the player overlay needs from the window that hosts it.</summary>
public interface IPlayerHost
{
    bool IsFullscreen { get; }
    bool IsAlwaysOnTop { get; set; }
    void ToggleFullscreen();
    void BeginWindowDrag();
    Task OpenFilesAsync(bool append = false);
    Task OpenFolderAsync();
    Task OpenUrlAsync();
    Task OpenSubtitleAsync();
    void ShowSettings();
    void CloseMedia();
    void OnControlsVisibilityChanged(bool visible);
}
