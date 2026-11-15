using System.Runtime.InteropServices;

namespace Otear.Interop;

/// <summary>
/// Startup warm-up: creates and releases a throwaway software (WARP) Direct3D 11 device on a background
/// thread. The window's title bar initialises WARP the first time it is set up; doing it ahead of time keeps
/// that work off the UI thread while the compositor initialises the GPU driver. (A hardware-device warm-up
/// was measured too: it only competes with the compositor's own device creation and makes startup slower.)
/// </summary>
internal static unsafe partial class GpuWarmup
{
    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint numFeatureLevels, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    /// <summary>Software (WARP) device: loads and initialises D3D10Warp.dll, which the title bar uses.</summary>
    public static Task StartWarp() => Task.Run(() =>
    {
        try
        {
            if (D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out nint device, out _, out nint context) >= 0)
            {
                if (context != 0) Marshal.Release(context);
                if (device != 0) Marshal.Release(device);
            }
            Otear.Services.StartupTrace.Mark("warpWarm");
        }
        catch { }
    });
}
