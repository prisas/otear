using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Otear.Interop;
using Otear.Player;
using Otear.Services;

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
    private static InputCursor? s_blankCursor;
    private static bool s_blankCursorFailed;
    private bool _cursorHidden;
    private readonly DispatcherQueueTimer _cursorCheck;
    private enum CursorCheck { Verify, Reapply, Done }
    private CursorCheck _cursorCheckStep;

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
            var cursor = value ? BlankCursor() : null;
            if (value && cursor is null) return;
            _cursorHidden = value;
            ProtectedCursor = cursor;
            _cursorCheck.Stop();
            if (value) ScheduleCursorCheck(150);
        }
    }

    private void ScheduleCursorCheck(int delayMs)
    {
        _cursorCheck.Stop();
        _cursorCheckStep = CursorCheck.Verify;
        _cursorCheck.Interval = TimeSpan.FromMilliseconds(delayMs);
        _cursorCheck.Start();
    }

    /// <summary>
    /// After hiding, checks that the pointer really disappeared. When the window changed under a still pointer
    /// (full screen on/off, auto-fit resize, a closed menu), Windows keeps the pointer image of whatever was under
    /// it before, and nothing short of real mouse input hands it back: then the pointer is moved one pixel
    /// aside and back, and the blank cursor applied again.
    /// </summary>
    private void OnCursorCheck()
    {
        if (!_cursorHidden) return;
        switch (_cursorCheckStep)
        {
            case CursorCheck.Verify:
                if (!Win32.IsPointerOverContent(WindowHandle) || !Win32.IsPointerVisible()) return;
                if (!Win32.NudgePointer()) return;
                _cursorCheckStep = CursorCheck.Reapply;
                _cursorCheck.Interval = TimeSpan.FromMilliseconds(50);
                _cursorCheck.Start();
                break;
            case CursorCheck.Reapply:
                // The injected moves have been delivered (timer ticks come after input). WinUI only pushes the
                // cursor to the screen when it changes, so go through the default cursor first.
                ProtectedCursor = null;
                _cursorCheckStep = CursorCheck.Done;
                _cursorCheck.Interval = TimeSpan.FromMilliseconds(50);
                _cursorCheck.Start();
                break;
            case CursorCheck.Done:
                ProtectedCursor = s_blankCursor;
                break;
        }
    }

    /// <summary>Stops the pointer check timer before the window is destroyed.</summary>
    public void Shutdown()
    {
        _cursorCheck.Stop();
        _cursorHidden = false;
    }

    private static InputCursor? BlankCursor()
    {
        if (s_blankCursor is null && !s_blankCursorFailed)
        {
            try
            {
                s_blankCursor = InputDesktopResourceCursor.CreateFromModule(Path.GetFileName(Environment.ProcessPath!), BlankCursorId);
            }
            catch (Exception ex)
            {
                s_blankCursorFailed = true;
                Log.Warn($"Blank cursor resource not available; the pointer will not auto-hide: {ex.Message}");
            }
        }
        return s_blankCursor;
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
