using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace Otear.Interop;

/// <summary>
/// Minimal, AOT-safe COM calls via vtable function pointers: binding a DXGI swap chain to a
/// WinUI SwapChainPanel and setting the swap chain's inverse-DPI matrix transform.
/// </summary>
internal static unsafe class ComInterop
{
    // microsoft.ui.xaml.media.dxinterop.h (WinUI 3) – ISwapChainPanelNative
    private static readonly Guid IID_ISwapChainPanelNative = new("63aad0b8-7c24-40ff-85a8-640d944cc325");
    // dxgi1_3.h – IDXGISwapChain2
    private static readonly Guid IID_IDXGISwapChain2 = new("a8be2ac4-199f-4946-b331-79599fb98de7");

    private const int SetSwapChainSlot = 3;          // IUnknown(3) → SetSwapChain
    private const int SetMatrixTransformSlot = 34;   // IDXGISwapChain2::SetMatrixTransform

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix3x2
    {
        public float M11, M12, M21, M22, M31, M32;
    }

    /// <summary>Passes <paramref name="swapChain"/> (an IDXGISwapChain*, may be 0) to the panel.</summary>
    public static void SetSwapChain(SwapChainPanel panel, nint swapChain)
    {
        nint unknown = ((IWinRTObject)panel).NativeObject.ThisPtr;
        Guid iid = IID_ISwapChainPanelNative;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out nint native));
        try
        {
            var fn = (delegate* unmanaged[Stdcall]<nint, nint, int>)(*(void***)native)[SetSwapChainSlot];
            Marshal.ThrowExceptionForHR(fn(native, swapChain));
        }
        finally
        {
            Marshal.Release(native);
        }
    }

    /// <summary>
    /// Scales the swap chain by 1/scale so that one swap chain pixel maps to one physical pixel
    /// inside a SwapChainPanel (which otherwise maps one swap chain pixel to one DIP).
    /// </summary>
    public static bool SetInverseScale(nint swapChain, float scaleX, float scaleY)
    {
        if (swapChain == 0 || scaleX <= 0 || scaleY <= 0) return false;
        Guid iid = IID_IDXGISwapChain2;
        if (Marshal.QueryInterface(swapChain, in iid, out nint sc2) < 0) return false;
        try
        {
            var m = new Matrix3x2 { M11 = 1f / scaleX, M22 = 1f / scaleY };
            var fn = (delegate* unmanaged[Stdcall]<nint, Matrix3x2*, int>)(*(void***)sc2)[SetMatrixTransformSlot];
            return fn(sc2, &m) >= 0;
        }
        finally
        {
            Marshal.Release(sc2);
        }
    }
}
