using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Otear.Interop;

namespace Otear.Services;

/// <summary>
/// Keeps the window's client area at the video's aspect ratio while the user resizes it
/// (WM_SIZING via a comctl32 window subclass), like IINA does on macOS.
/// </summary>
internal sealed unsafe class WindowSizing : IDisposable
{
    private readonly nint _hwnd;
    private GCHandle _self;
    private bool _installed;

    /// <summary>Client width / height to keep, or 0 to resize freely.</summary>
    public double AspectRatio { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Current size of the area the video fills, in pixels (defaults to the Win32 client rect).</summary>
    public Func<(int Width, int Height)>? ContentSize { get; set; }

    public WindowSizing(nint hwnd)
    {
        _hwnd = hwnd;
        _self = GCHandle.Alloc(this);
        _installed = Win32.SetWindowSubclass(hwnd, &SubclassProc, 1, (nuint)GCHandle.ToIntPtr(_self));
        if (!_installed) Log.Warn("SetWindowSubclass failed; aspect-locked resizing disabled");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (msg == Win32.WM_SIZING && refData != 0)
        {
            try
            {
                if (GCHandle.FromIntPtr((nint)refData).Target is WindowSizing self && self.OnSizing((int)wParam, (Win32.RECT*)lParam))
                    return 1;
            }
            catch
            {
                // Never let an exception cross the native boundary.
            }
        }
        return Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private bool OnSizing(int edge, Win32.RECT* r)
    {
        if (!Enabled || AspectRatio <= 0.05 || AspectRatio > 20) return false;
        if (!Win32.GetWindowRect(_hwnd, out var wr)) return false;
        int contentW, contentH;
        if (ContentSize is { } provider) (contentW, contentH) = provider();
        else if (Win32.GetClientRect(_hwnd, out var cr)) (contentW, contentH) = (cr.Width, cr.Height);
        else return false;
        if (contentW <= 0 || contentH <= 0) return false;
        int frameW = wr.Width - contentW, frameH = wr.Height - contentH;
        int cw = r->Width - frameW, ch = r->Height - frameH;
        double ar = AspectRatio;

        switch (edge)
        {
            case Win32.WMSZ_LEFT:
            case Win32.WMSZ_RIGHT:
                r->Bottom = r->Top + (int)Math.Round(cw / ar) + frameH;
                break;
            case Win32.WMSZ_TOP:
            case Win32.WMSZ_BOTTOM:
                r->Right = r->Left + (int)Math.Round(ch * ar) + frameW;
                break;
            case Win32.WMSZ_TOPLEFT:
            case Win32.WMSZ_TOPRIGHT:
                r->Top = r->Bottom - (int)Math.Round(cw / ar) - frameH;
                break;
            case Win32.WMSZ_BOTTOMLEFT:
            case Win32.WMSZ_BOTTOMRIGHT:
                r->Bottom = r->Top + (int)Math.Round(cw / ar) + frameH;
                break;
            default:
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_installed) Win32.RemoveWindowSubclass(_hwnd, &SubclassProc, 1);
        _installed = false;
        if (_self.IsAllocated) _self.Free();
    }
}
