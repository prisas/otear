using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Otear.Player;
using Otear.Services;
using Otear.Views;
using Windows.UI.Text;

namespace Otear.Controls;

/// <summary>A row whose "current" highlight can change without rebuilding the list.</summary>
public abstract partial class CurrentRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (!SetProperty(ref _isCurrent, value)) return;
            OnPropertyChanged(nameof(Weight));
            OnPropertyChanged(nameof(CurrentVisibility));
            OnPropertyChanged(nameof(NumberVisibility));
        }
    }
    public FontWeight Weight => IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NumberVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;
}

public sealed partial class PlaylistRow : CurrentRow
{
    public int Index { get; init; }
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Number => (Index + 1).ToString(CultureInfo.InvariantCulture);
}

public sealed partial class ChapterRow : CurrentRow
{
    public int Index { get; init; }
    public string Title { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
}

/// <summary>Playlist (reorder, remove, add) and chapter list side panel.</summary>
public sealed partial class PlaylistPanel : UserControl
{
    private PlayerController? _player;
    private IPlayerHost? _host;
    private readonly ObservableCollection<PlaylistRow> _rows = [];
    private readonly ObservableCollection<ChapterRow> _chapters = [];
    private int _dragFrom = -1;

    public event Action? CloseRequested;

    public PlaylistPanel()
    {
        InitializeComponent();
        PlaylistList.ItemsSource = _rows;
        ChapterList.ItemsSource = _chapters;
    }

    public void Attach(PlayerController player, IPlayerHost host)
    {
        _player = player;
        _host = host;
        player.PropertyChanged += OnPlayerChanged;
    }

    private bool ShowingChapters => Tabs.SelectedItem == ChaptersTab;

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible) return;
        if (e.PropertyName is nameof(PlayerController.Playlist) or nameof(PlayerController.PlaylistPosition)
            or nameof(PlayerController.Chapters) or nameof(PlayerController.Chapter))
            Refresh();
    }

    private IReadOnlyList<Otear.Mpv.MpvChapter>? _shownChapters;
    private double _shownDuration;

    public void Refresh()
    {
        var p = _player;
        if (p is null) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        // Rebuild rows only when the entries themselves changed; otherwise just move the highlight.
        bool sameEntries = _rows.Count == p.Playlist.Count;
        for (int i = 0; sameEntries && i < _rows.Count; i++)
            sameEntries = _rows[i].Id == p.Playlist[i].Id && _rows[i].Path == p.Playlist[i].Filename;
        if (!sameEntries)
        {
            _rows.Clear();
            foreach (var e in p.Playlist)
                _rows.Add(new PlaylistRow { Index = e.Index, Id = e.Id, Name = e.DisplayName, Path = e.Filename });
        }
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].IsCurrent = p.Playlist[i].IsCurrent || i == p.PlaylistPosition;

        if (!ReferenceEquals(_shownChapters, p.Chapters) || _shownDuration != p.Duration)
        {
            _shownChapters = p.Chapters;
            _shownDuration = p.Duration;
            _chapters.Clear();
            foreach (var c in p.Chapters)
                _chapters.Add(new ChapterRow { Index = c.Index, Title = c.Title, TimeText = TimeFormat.Format(c.Time, p.Duration) });
        }
        for (int i = 0; i < _chapters.Count; i++)
            _chapters[i].IsCurrent = i == p.Chapter;

        UpdateVisibility();
        PerfCounters.PlaylistRefreshes++;
        PerfCounters.PlaylistRefreshTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    private void UpdateVisibility()
    {
        bool chapters = ShowingChapters;
        PlaylistList.Visibility = !chapters ? Visibility.Visible : Visibility.Collapsed;
        ChapterList.Visibility = chapters ? Visibility.Visible : Visibility.Collapsed;
        PlaylistActions.Visibility = !chapters ? Visibility.Visible : Visibility.Collapsed;
        bool empty = chapters ? _chapters.Count == 0 : _rows.Count == 0;
        EmptyText.Text = chapters ? "This file has no chapters" : "The playlist is empty";
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => UpdateVisibility();

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnPlaylistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistRow row && !row.IsCurrent) _player?.PlaylistPlay(row.Index);
    }

    private void OnChapterItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ChapterRow row) _player?.SetChapter(row.Index);
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index }) _player?.PlaylistRemove(index);
    }

    private async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        if (_host is not null) await _host.OpenFilesAsync(append: true);
    }

    private void OnClearPlaylist(object sender, RoutedEventArgs e) => _player?.PlaylistClear();

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragFrom = e.Items.FirstOrDefault() is PlaylistRow row ? _rows.IndexOf(row) : -1;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_dragFrom < 0 || args.Items.FirstOrDefault() is not PlaylistRow row) return;
        int to = _rows.IndexOf(row);
        int from = _dragFrom;
        _dragFrom = -1;
        if (to < 0 || to == from) return;
        // mpv's playlist-move inserts *before* the target entry.
        _player?.PlaylistMove(from, to > from ? to + 1 : to);
    }
}
