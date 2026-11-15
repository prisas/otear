using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Otear.Mpv;
using Otear.Services;

namespace Otear.Controls;

/// <summary>
/// IINA-style seek bar: played and buffered ranges, chapter gaps, a knob on hover, and a hover card
/// with time, chapter name and (optionally) a thumbnail. Dragging seeks live (keyframes), release seeks exactly.
/// </summary>
public sealed partial class SeekBar : UserControl
{
    private double _position, _duration;
    private IReadOnlyList<MpvChapter> _chapters = [];
    private IReadOnlyList<MpvSeekRange> _ranges = [];
    private bool _hovering, _dragging;
    private double _bufferedHeight;
    private double _dragTime;

    /// <summary>(time, exact): user dragged or clicked.</summary>
    public event Action<double, bool>? SeekRequested;
    public event Action<bool>? DraggingChanged;

    /// <summary>Returns a thumbnail for a time, or null when none is available (yet).</summary>
    public Func<double, ImageSource?>? ThumbnailProvider { get; set; }
    public double ThumbnailWidth { get; set; } = 192;

    public bool IsDragging => _dragging;
    public bool IsHovering => _hovering;

    public SeekBar()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => Relayout();
        ActualThemeChanged += (_, _) =>
        {
            _tickBrush = _bufferedBrush = null;
            Relayout();
        };
    }

    private bool IsDark => ActualTheme == ElementTheme.Dark;
    private Brush? _tickBrush, _bufferedBrush;
    private Brush TickBrush => _tickBrush ??= new SolidColorBrush(IsDark ? Windows.UI.Color.FromArgb(0xB0, 0, 0, 0) : Windows.UI.Color.FromArgb(0xC0, 255, 255, 255));
    private Brush BufferedBrush => _bufferedBrush ??= new SolidColorBrush(IsDark ? Windows.UI.Color.FromArgb(0x60, 255, 255, 255) : Windows.UI.Color.FromArgb(0x40, 0, 0, 0));

    public void Update(double position, double duration)
    {
        bool durationChanged = Math.Abs(duration - _duration) > 0.001;
        _position = position;
        _duration = duration;
        if (durationChanged) { DrawTicks(); DrawBuffered(); }
        if (!_dragging) UpdatePlayed(position);
    }

    public void SetChapters(IReadOnlyList<MpvChapter> chapters)
    {
        _chapters = chapters;
        DrawTicks();
    }

    public void SetBuffered(IReadOnlyList<MpvSeekRange> ranges)
    {
        _ranges = ranges;
        DrawBuffered();
    }

    private double Width0 => Math.Max(1, Root.ActualWidth);

    private double TimeToX(double t) => _duration > 0 ? Math.Clamp(t / _duration, 0, 1) * Width0 : 0;
    private double XToTime(double x) => _duration > 0 ? Math.Clamp(x / Width0, 0, 1) * _duration : 0;

    private void Relayout()
    {
        _playedX = double.NaN;
        _bufferedSpans.Clear();
        UpdatePlayed(_dragging ? _dragTime : _position);
        DrawTicks();
        DrawBuffered();
    }

    private double _playedX = double.NaN;
    private readonly List<(int X0, int X1)> _bufferedSpans = [];

    private void UpdatePlayed(double t)
    {
        // During playback this is called every frame; only touch the visual tree when the bar moves
        // by at least half a pixel (for a long film that is a few times a minute).
        double x = TimeToX(t);
        if (Math.Abs(x - _playedX) < 0.5) return;
        _playedX = x;
        PlayedRect.Width = x;
        ThumbTransform.X = x - Thumb.Width / 2;
    }

    private void DrawTicks()
    {
        PerfCounters.SeekBarRedraws++;
        TickCanvas.Children.Clear();
        if (_duration <= 0 || _chapters.Count < 2) return;
        var brush = TickBrush;
        foreach (var c in _chapters)
        {
            if (c.Time <= 0.5 || c.Time >= _duration - 0.5) continue;
            var r = new Rectangle { Width = 2, Height = 8, Fill = brush };
            Canvas.SetLeft(r, TimeToX(c.Time) - 1);
            Canvas.SetTop(r, -2);
            TickCanvas.Children.Add(r);
        }
    }

    private void DrawBuffered()
    {
        // Skip redraws that would produce the same pixels (cache state updates arrive often while streaming).
        var spans = new List<(int, int)>(_ranges.Count);
        if (_duration > 0)
            foreach (var range in _ranges)
            {
                int x0 = (int)TimeToX(range.Start), x1 = (int)TimeToX(range.End);
                if (x1 - x0 >= 1) spans.Add((x0, x1));
            }
        if (spans.SequenceEqual(_bufferedSpans) && BufferCanvas.Children.Count == spans.Count && Bar.Height == _bufferedHeight) return;
        _bufferedSpans.Clear();
        _bufferedSpans.AddRange(spans);
        _bufferedHeight = Bar.Height;
        PerfCounters.SeekBarRedraws++;
        BufferCanvas.Children.Clear();
        if (_duration <= 0) return;
        var brush = BufferedBrush;
        foreach (var range in _ranges)
        {
            double x0 = TimeToX(range.Start), x1 = TimeToX(range.End);
            if (x1 - x0 < 1) continue;
            var r = new Rectangle { Width = x1 - x0, Height = Bar.Height, Fill = brush, RadiusX = 2, RadiusY = 2 };
            Canvas.SetLeft(r, x0);
            BufferCanvas.Children.Add(r);
        }
    }

    private void SetExpanded(bool expanded)
    {
        Bar.Height = expanded ? 6 : 4;
        Thumb.Opacity = expanded ? 1 : 0;
        DrawBuffered();
    }

    // ------------------------------------------------------------------ pointer

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovering = true;
        SetExpanded(true);
        UpdateHover(e.GetCurrentPoint(Root).Position.X);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovering = false;
        if (!_dragging)
        {
            SetExpanded(false);
            HoverPopup.IsOpen = false;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(Root).Position.X;
        UpdateHover(x);
        if (_dragging)
        {
            _dragTime = XToTime(x);
            UpdatePlayed(_dragTime);
            SeekRequested?.Invoke(_dragTime, false);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_duration <= 0) return;
        var pt = e.GetCurrentPoint(Root);
        if (!pt.Properties.IsLeftButtonPressed) return;
        _dragging = Root.CapturePointer(e.Pointer);
        _dragTime = XToTime(pt.Position.X);
        UpdatePlayed(_dragTime);
        SeekRequested?.Invoke(_dragTime, false);
        DraggingChanged?.Invoke(true);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragTime = XToTime(e.GetCurrentPoint(Root).Position.X);
        EndDrag();
        Root.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging) EndDrag();
    }

    private void EndDrag()
    {
        _dragging = false;
        _position = _dragTime;
        UpdatePlayed(_dragTime);
        SeekRequested?.Invoke(_dragTime, true);
        DraggingChanged?.Invoke(false);
        if (!_hovering)
        {
            SetExpanded(false);
            HoverPopup.IsOpen = false;
        }
    }

    private void UpdateHover(double x)
    {
        if (_duration <= 0) { HoverPopup.IsOpen = false; return; }
        double t = XToTime(x);
        HoverTime.Text = TimeFormat.Format(t, _duration);

        var chapter = _chapters.LastOrDefault(c => c.Time <= t + 0.01);
        if (chapter is not null && _chapters.Count > 1)
        {
            HoverChapter.Text = chapter.Title;
            HoverChapter.Visibility = Visibility.Visible;
        }
        else
        {
            HoverChapter.Visibility = Visibility.Collapsed;
        }

        var thumb = ThumbnailProvider?.Invoke(t);
        if (thumb is not null)
        {
            ThumbImage.Source = thumb;
            ThumbImage.Width = ThumbnailWidth;
            ThumbFrame.Visibility = Visibility.Visible;
        }
        else
        {
            ThumbFrame.Visibility = Visibility.Collapsed;
        }

        HoverCard.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverCard.DesiredSize;
        HoverPopup.HorizontalOffset = Math.Clamp(x - size.Width / 2, -8, Width0 - size.Width + 8);
        // Above the bar, or below it when the control bar has been moved to the top of the window.
        double top = TransformToVisual(null).TransformPoint(default).Y;
        HoverPopup.VerticalOffset = top - size.Height - 6 >= 4 ? -size.Height - 6 : ActualHeight + 6;
        HoverPopup.IsOpen = true;
    }

#if DEBUG
    internal UIElement TourHoverCard => HoverCard;
    internal void TourHover(double fraction)
    {
        _hovering = true;
        SetExpanded(true);
        UpdateHover(fraction * Width0);
    }
#endif
}