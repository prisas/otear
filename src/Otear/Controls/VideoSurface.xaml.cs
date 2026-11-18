using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Otear.Interop;
using Otear.Player;
using Otear.Services;
using Windows.Foundation;

namespace Otear.Controls;

/// <summary>
/// Hosts mpv's composition swap chain in a SwapChainPanel and keeps mpv's render size equal to the
/// panel's size in physical pixels.
/// </summary>
public sealed partial class VideoSurface : UserControl
{
    private PlayerController? _player;
    private nint _bound;
    private static readonly SolidColorBrush s_black = new(Colors.Black);

    public VideoSurface()
    {
        InitializeComponent();
        Panel.SizeChanged += (_, _) =>
        {
            ReportSize();
            // Full screen on/off or a resize under a hidden pointer: make sure it stays hidden afterwards.
            if (_cursorHidden) ScheduleCursorCheck(300);
        };
        Panel.CompositionScaleChanged += (_, _) =>
        {
            ReportSize();
            ApplyScale();
        };
        _cursorCheck = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _cursorCheck.IsRepeating = false;
        _cursorCheck.Tick += (_, _) => OnCursorCheck();
    }

    /// <summary>The top-level window, used to check that the pointer is really over the video.</summary>
    public nint WindowHandle { get; set; }

    public void Attach(PlayerController player)
    {
        _player = player;
        player.SwapChainChanged += Bind;
        ReportSize();
    }

    /// <summary>When inactive the surface is transparent so the window's Mica shows through.</summary>
    public bool IsActive
    {
        get => Root.Background is not null;
        set
        {
            Root.Background = value ? s_black : null;
            Panel.Opacity = value ? 1 : 0;
            if (!value) Clear();
        }
    }

    private void ReportSize()
    {
        if (_player is null) return;
        int w = (int)Math.Round(Panel.ActualWidth * Panel.CompositionScaleX);
        int h = (int)Math.Round(Panel.ActualHeight * Panel.CompositionScaleY);
        if (w > 0 && h > 0) _player.SetCompositionSize(w, h);
    }

    private void Bind(nint swapChain)
    {
        if (swapChain == _bound) return;
        try
        {
            ComInterop.SetSwapChain(Panel, swapChain);
            _bound = swapChain;
            ApplyScale();
        }
        catch (Exception ex)
        {
            Log.Error("Binding mpv swap chain failed", ex);
        }
    }

    private void ApplyScale()
    {
        if (_bound != 0) ComInterop.SetInverseScale(_bound, Panel.CompositionScaleX, Panel.CompositionScaleY);
    }

    // ------------------------------------------------------------------ pointer

    /// <summary>Id of the blank cursor that build/BlankCursor.targets adds to Otear.exe.</summary>
    private const uint BlankCursorId = 101;
    // Two cursor objects for the same blank image: switching between them is a change WinUI passes on to the
    // screen, without showing the arrow in between.
    private static readonly InputCursor?[] s_blankCursors = new InputCursor?[2];
    private static bool s_blankCursorFailed;
    // Blank cursor handles seen on screen: when one of these is shown, the pointer is hidden and there is
    // nothing to check.
    private static readonly nint[] s_blankHandles = new nint[2];
    private bool _cursorHidden;
    private int _blankIndex;
    private readonly DispatcherQueueTimer _cursorCheck;
    private enum Reclaim { None, Nudged, Reapplied }
    private Reclaim _reclaim;
    private int _reclaimCount;
    private long _lastReclaim;

    /// <summary>
    /// Hides the mouse pointer while it is over the video. WinUI's input stack owns the pointer image, so the
    /// Win32 ShowCursor/SetCursor calls have no effect here: the cursor has to be an InputCursor of this element.
    /// </summary>
    public bool IsCursorHidden
    {
        get => _cursorHidden;
        set
        {
            if (_cursorHidden == value) return;
            var cursor = value ? BlankCursor(_blankIndex) : null;
            if (value && cursor is null) return;
            _cursorHidden = value;
            _cursorCheck.Stop();
            _reclaim = Reclaim.None;
            _reclaimCount = 0;
            ProtectedCursor = cursor;
            if (value) ScheduleCursorCheck(150);
        }
    }

    /// <summary>
    /// True while the pointer is being moved one pixel aside and back to take it back (see
    /// <see cref="EnsureCursorHidden"/>): those moves are not the user's.
    /// </summary>
    public bool IsReclaimingPointer => _reclaim != Reclaim.None;

    private void ScheduleCursorCheck(int delayMs)
    {
        if (_reclaim != Reclaim.None) return;
        _cursorCheck.Stop();
        _cursorCheck.Interval = TimeSpan.FromMilliseconds(delayMs);
        _cursorCheck.Start();
    }

    /// <summary>
    /// Makes sure a hidden pointer really is invisible; called after hiding and then regularly while hidden.
    /// WinUI only puts the element's cursor on screen when the cursor changes or the pointer moves. When
    /// something else changed the pointer image under a still pointer (full screen on/off, a resize, a closed
    /// menu or popup, another window), it stays that way, and nothing short of real mouse input hands it back:
    /// then the pointer is moved one pixel aside and back, and the blank cursor applied again.
    /// The common case, the blank cursor on screen, costs one GetCursorInfo call.
    /// </summary>
    public void EnsureCursorHidden()
    {
        if (!_cursorHidden || _reclaim != Reclaim.None) return;
        if (!Win32.TryGetShownCursor(out nint shown) || shown == s_blankHandles[0] || shown == s_blankHandles[1]) return;
        // Only where this element's cursor applies: over the video, not the title bar, another window, a menu or
        // another element on top of the video.
        if (XamlRoot is not { } root || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0) return;
        if (!Win32.TryGetPointerOverContent(WindowHandle, out int x, out int y)) return;
        double scale = root.RasterizationScale;
        if (!IsTopmostAt(new Point(x / scale, y / scale), root)) return;
        if (!Win32.IsCursorVisible(shown))
        {
            // Our blank cursor (or another invisible one): remember it so the next checks are cheap.
            s_blankHandles[s_blankHandles[0] == 0 ? 0 : 1] = shown;
            return;
        }
        // Not during a click or drag, and without fighting something that keeps putting the arrow back.
        if (Win32.IsAnyMouseButtonDown() || _reclaimCount >= 3 || Environment.TickCount64 - _lastReclaim < 700) return;
        if (!Win32.NudgePointer()) return;
        _reclaimCount++;
        _lastReclaim = Environment.TickCount64;
        _reclaim = Reclaim.Nudged;
        _cursorCheck.Stop();
        _cursorCheck.Interval = TimeSpan.FromMilliseconds(50);
        _cursorCheck.Start();
    }

    private void OnCursorCheck()
    {
        switch (_reclaim)
        {
            case Reclaim.Nudged:
                // The injected moves have been delivered (timer ticks come after input) and the pointer belongs to
                // the video again. Re-apply the blank cursor as a change, so WinUI puts it on screen.
                _reclaim = Reclaim.Reapplied;
                if (_cursorHidden && BlankCursor(_blankIndex ^ 1) is { } other)
                {
                    _blankIndex ^= 1;
                    ProtectedCursor = other;
                }
                _cursorCheck.Interval = TimeSpan.FromMilliseconds(50);
                _cursorCheck.Start();
                break;
            case Reclaim.Reapplied:
                _reclaim = Reclaim.None;
                EnsureCursorHidden();
                break;
            default:
                EnsureCursorHidden();
                break;
        }
    }

    /// <summary>True when the topmost hit-testable element at <paramref name="point"/> is (inside) this one.</summary>
    private bool IsTopmostAt(Point point, XamlRoot root)
    {
        if (root.Content is not UIElement content) return false;
        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(point, content))
        {
            for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
                if (ReferenceEquals(d, this)) return true;
            return false;
        }
        return false;
    }

    /// <summary>Stops the pointer check timer before the window is destroyed.</summary>
    public void Shutdown()
    {
        _cursorCheck.Stop();
        _cursorHidden = false;
        _reclaim = Reclaim.None;
    }

    private static InputCursor? BlankCursor(int index)
    {
        if (s_blankCursors[index] is null && !s_blankCursorFailed)
        {
            try
            {
                s_blankCursors[index] = InputDesktopResourceCursor.CreateFromModule(Path.GetFileName(Environment.ProcessPath!), BlankCursorId);
            }
            catch (Exception ex)
            {
                s_blankCursorFailed = true;
                Log.Warn($"Blank cursor resource not available; the pointer will not auto-hide: {ex.Message}");
            }
        }
        return s_blankCursors[index];
    }
    /// <summary>Detaches the last frame (e.g. when returning to the welcome screen).</summary>
    public void Clear()
    {
        if (_bound == 0) return;
        try { ComInterop.SetSwapChain(Panel, 0); }
        catch (Exception ex) { Log.Error("Clearing swap chain failed", ex); }
        _bound = 0;
    }
}
