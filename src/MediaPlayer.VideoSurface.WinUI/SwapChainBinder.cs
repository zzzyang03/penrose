using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace MediaPlayer.VideoSurface.WinUI;

[ComImport]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(nint swapChain);
}

public static class SwapChainBinder
{
    // IDXGISwapChain2
    private static readonly Guid SwapChain2Iid = new("a8be2ac4-199f-4946-b331-79599fb98de7");

    // IUnknown 0-2, IDXGIObject 3-6, IDXGIDeviceSubObject 7, IDXGISwapChain 8-17,
    // IDXGISwapChain1 18-28, IDXGISwapChain2: SetSourceSize 29, GetSourceSize 30,
    // SetMaximumFrameLatency 31, GetMaximumFrameLatency 32,
    // GetFrameLatencyWaitableObject 33, SetMatrixTransform 34.
    private const int SetMatrixTransformSlot = 34;

    public static void SetSwapChain(SwapChainPanel panel, nint swapChain)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ISwapChainPanelNative native = panel.As<ISwapChainPanelNative>();
        int hr = native.SetSwapChain(swapChain);
        Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>
    /// A SwapChainPanel scales its content by the composition scale. mpv sizes the
    /// swap chain in physical pixels (<c>d3d11-composition-size</c>), so without
    /// the inverse transform a 150 % desktop shows the video magnified 1.5× and
    /// cropped. Best effort: the pointer is borrowed from mpv and only queried.
    /// </summary>
    public static bool TrySetInverseScale(nint swapChain, double scaleX, double scaleY)
    {
        if (swapChain == 0 || scaleX <= 0 || scaleY <= 0)
        {
            return false;
        }

        nint vtbl = Marshal.ReadIntPtr(swapChain);
        QueryInterfaceDelegate qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
            Marshal.ReadIntPtr(vtbl, 0));
        Guid iid = SwapChain2Iid;
        if (qi(swapChain, ref iid, out nint swapChain2) < 0 || swapChain2 == 0)
        {
            return false;
        }

        try
        {
            nint vtbl2 = Marshal.ReadIntPtr(swapChain2);
            SetMatrixTransformDelegate setMatrix = Marshal.GetDelegateForFunctionPointer<SetMatrixTransformDelegate>(
                Marshal.ReadIntPtr(vtbl2, SetMatrixTransformSlot * nint.Size));
            DxgiMatrix3x2 matrix = new()
            {
                M11 = (float)(1.0 / scaleX),
                M22 = (float)(1.0 / scaleY),
            };
            return setMatrix(swapChain2, ref matrix) >= 0;
        }
        finally
        {
            ReleaseDelegate release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(swapChain2), 2 * nint.Size));
            _ = release(swapChain2);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(nint obj, ref Guid iid, out nint ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint punk);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetMatrixTransformDelegate(nint swapChain, ref DxgiMatrix3x2 matrix);

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiMatrix3x2
    {
        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float M31;
        public float M32;
    }
}
