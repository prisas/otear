using System.Runtime.InteropServices;

namespace Otear.Interop;

/// <summary>Mouse-pointer helpers used to verify (and, if needed, re-establish) the auto-hidden pointer.</summary>
internal static unsafe partial class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public nint hCursor;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    private const int CURSOR_SHOWING = 1;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000,
                       MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const uint GA_ROOT = 2;
    private const int DI_NORMAL = 3;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CURSORINFO info);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DrawIconEx(nint hdc, int x, int y, nint icon, int cx, int cy, uint step, nint brush, int flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    /// <summary>
    /// True when the pointer is over the XAML content (not the title bar) of <paramref name="window"/> and that
    /// window is in the foreground, i.e. where a hidden pointer is expected to be invisible.
    /// </summary>
    public static bool IsPointerOverContent(nint window)
    {
        if (window == 0 || GetForegroundWindow() != window || !GetCursorPos(out var pt)) return false;
        nint under = WindowFromPoint(pt);
        if (under == 0 || GetAncestor(under, GA_ROOT) != window) return false;
        char* name = stackalloc char[64];
        int n = GetClassName(under, name, 64);
        return new ReadOnlySpan<char>(name, n).SequenceEqual("Microsoft.UI.Content.DesktopChildSiteBridge");
    }

    /// <summary>True when the pointer currently drawn on screen has any visible pixel.</summary>
    public static bool IsPointerVisible()
    {
        var info = new CURSORINFO { cbSize = sizeof(CURSORINFO) };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || info.hCursor == 0) return false;

        // Draw the cursor onto two different backgrounds: a blank cursor leaves both untouched.
        const int Size = 256; // the largest accessibility pointer size
        var header = new BITMAPINFOHEADER { biSize = sizeof(BITMAPINFOHEADER), biWidth = Size, biHeight = -Size, biPlanes = 1, biBitCount = 32 };
        nint dc = CreateCompatibleDC(0);
        nint bitmap = CreateDIBSection(dc, &header, 0, out nint bits, 0, 0);
        if (dc == 0 || bitmap == 0)
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (dc != 0) DeleteDC(dc);
            return true;
        }
        nint old = SelectObject(dc, bitmap);
        try
        {
            var pixels = new Span<uint>((void*)bits, Size * Size);
            foreach (uint background in (ReadOnlySpan<uint>)[0x0000FF00u, 0x00FF00FFu])
            {
                pixels.Fill(background);
                DrawIconEx(dc, 0, 0, info.hCursor, 0, 0, 0, 0, DI_NORMAL);
                // Colour only: GDI's mask drawing rewrites the (unused) alpha byte even where nothing shows, so
                // comparing whole pixels took the blank cursor for a visible one.
                foreach (uint pixel in pixels)
                    if ((pixel & 0x00FFFFFFu) != background) return true;
            }
            return false;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    /// <summary>
    /// Moves the pointer one pixel aside and back with injected mouse input. A window that appeared or changed
    /// size under a still pointer does not own the pointer image until the pointer really moves: SetCursorPos
    /// is not enough for WinUI's input stack (and even undoes it), and a move that goes nowhere is dropped.
    /// Absolute coordinates avoid pointer acceleration, so the pointer ends exactly where it was.
    /// </summary>
    public static bool NudgePointer()
    {
        if (!GetCursorPos(out var p)) return false;
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN), height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 1 || height <= 1) return false;
        int aside = p.X + 1 < left + width ? p.X + 1 : p.X - 1;
        // Normalised 0..65535 coordinates aimed at the centre of the target pixel land on it exactly.
        static int Normalise(int v, int origin, int size) => (int)Math.Round((v - origin + 0.5) * 65536.0 / size);
        const uint Flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_MOVE_NOCOALESCE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_ABSOLUTE;
        int y = Normalise(p.Y, top, height);
        INPUT* inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = Normalise(aside, left, width), dy = y, dwFlags = Flags } };
        inputs[1] = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = Normalise(p.X, left, width), dy = y, dwFlags = Flags } };
        return SendInput(2, inputs, sizeof(INPUT)) == 2;
    }
}
