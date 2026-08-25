using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ArknightsRecruitRecommender.Interop;

/// <summary>
/// Raw interop needed to bridge Win32 (HWND, ID3D11Device) into the WinRT
/// Windows.Graphics.Capture APIs. There is no managed wrapper for this in .NET, so it has to
/// be done by hand. This is the most version-fragile part of the app - if it stops compiling
/// after a CsWinRT/.NET SDK upgrade, this is the first place to look.
/// </summary>
internal static class GraphicsCaptureInterop
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemGuid = typeof(GraphicsCaptureItem).GUID;
        var itemPointer = interop.CreateForWindow(hwnd, ref itemGuid);
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", PreserveSig = false)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public static IDirect3DDevice CreateDirect3DDeviceFromD3D11Device(IntPtr d3d11DevicePointer)
    {
        var iid = IID_IDXGIDevice;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d11DevicePointer, ref iid, out var dxgiDevicePointer));
        try
        {
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePointer, out var graphicsDevicePointer);
            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePointer);
            }
            finally
            {
                Marshal.Release(graphicsDevicePointer);
            }
        }
        finally
        {
            Marshal.Release(dxgiDevicePointer);
        }
    }

    [DllImport("d3d11.dll")]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        D3D_DRIVER_TYPE driverType,
        IntPtr software,
        uint flags,
        [In] D3D_FEATURE_LEVEL[]? featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out D3D_FEATURE_LEVEL chosenFeatureLevel,
        out IntPtr immediateContext);

    public enum D3D_DRIVER_TYPE
    {
        D3D_DRIVER_TYPE_HARDWARE = 1,
    }

    public enum D3D_FEATURE_LEVEL
    {
        D3D_FEATURE_LEVEL_11_0 = 0xb000,
    }

    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

    public static IntPtr CreateD3D11Device()
    {
        var featureLevels = new[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 };
        var hr = D3D11CreateDevice(
            IntPtr.Zero,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels,
            (uint)featureLevels.Length,
            7, // D3D11_SDK_VERSION
            out var device,
            out _,
            out var context);

        Marshal.ThrowExceptionForHR(hr);
        Marshal.Release(context);
        return device;
    }
}
