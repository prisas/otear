using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Otear.Services;

namespace Otear.Views;

/// <summary>Start screen: open buttons and the recent-files list.</summary>
public sealed partial class WelcomeView : UserControl
{
    private IPlayerHost? _host;
    private RecentFilesService? _recent;

    public event Action<string>? RecentRequested;

    public WelcomeView()
    {
        InitializeComponent();
        try { LogoImage.Source = new BitmapImage(new Uri(AppPaths.Asset("Logo.png"))); }
        catch (Exception ex) { Log.Warn($"Logo not loaded: {ex.Message}"); }
    }

    internal void Attach(IPlayerHost host, RecentFilesService recent)
    {
        _host = host;
        _recent = recent;
        // Filled right after the first frame: the list is not needed for the window to appear (~9 ms sooner).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RecentList.ItemsSource = recent.Items);
        recent.Items.CollectionChanged += (_, _) => UpdateRecentVisibility();
        UpdateRecentVisibility();
    }

    private void UpdateRecentVisibility() =>
        RecentSection.Visibility = _recent?.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnOpenFile(object sender, RoutedEventArgs e) { if (_host is not null) await _host.OpenFilesAsync(); }
    private async void OnOpenFolder(object sender, RoutedEventArgs e) { if (_host is not null) await _host.OpenFolderAsync(); }
    private async void OnOpenUrl(object sender, RoutedEventArgs e) { if (_host is not null) await _host.OpenUrlAsync(); }
    private void OnSettings(object sender, RoutedEventArgs e) => _host?.ShowSettings();
    private void OnClearRecent(object sender, RoutedEventArgs e) => _recent?.Clear();

    private void OnRecentClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentItem item) RecentRequested?.Invoke(item.Path);
    }

    private void OnRemoveRecent(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path }) _recent?.Remove(path);
    }
}
