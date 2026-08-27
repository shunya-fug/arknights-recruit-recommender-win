using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
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
internal static partial class GraphicsCaptureInterop
{
    // IGraphicsCaptureItemInteropはWinRTメタデータ(winmd)に存在しない素のネイティブCOM
    // インターフェースであり、GraphicsCaptureItemの「インスタンス」ではなく「アクティベーション
    // ファクトリ」が実装している。GraphicsCaptureItem.As<T>()はCsWinRTのメタデータベースの
    // 投影機構でありwinmdに無いインターフェースは解決できず、実機ではE_NOINTERFACEになることを
    // 確認した。正しくはRoGetActivationFactoryで明示的にこのIIDを指定してファクトリを取得する
    // 必要がある(参考実装: https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/
    // blob/master/dotnet/WPF/ScreenCapture/Composition.WindowsRuntimeHelpers/CaptureHelper.cs
    // ただしこのサンプルは.NET Framework専用のWindowsRuntimeMarshalを使っており.NET 8では
    // 使えないため、RoGetActivationFactoryを直接P/Invokeし、[GeneratedComInterface]の
    // ComInterfaceMarshallerで管理オブジェクト化する)。
    [GeneratedComInterface]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    internal partial interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    private static unsafe IGraphicsCaptureItemInterop GetGraphicsCaptureItemInterop()
    {
        const string ClassId = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(ClassId, ClassId.Length, out var classIdHandle);
        try
        {
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            RoGetActivationFactory(classIdHandle, ref iid, out var factoryPointer);
            try
            {
                return ComInterfaceMarshaller<IGraphicsCaptureItemInterop>.ConvertToManaged((void*)factoryPointer)!;
            }
            finally
            {
                Marshal.Release(factoryPointer);
            }
        }
        finally
        {
            WindowsDeleteString(classIdHandle);
        }
    }

    // typeof(GraphicsCaptureItem).GUIDはCsWinRTが投影用に生成した無関係なGUIDであり、
    // ネイティブ側が期待するIID(GraphicsCaptureItemのデフォルトインターフェースIGraphicsCaptureItemの
    // IID)とは一致しない。これを渡すとCreateForWindowがインターフェイスがサポートされていません
    // (E_NOINTERFACE)を返すことを実機検証で確認した。正しいIIDはMicrosoft公式サンプルの
    // https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/dotnet/WPF/
    // ScreenCapture/Composition.WindowsRuntimeHelpers/CaptureHelper.cs にハードコードされている値。
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GetGraphicsCaptureItemInterop();
        var itemGuid = GraphicsCaptureItemIid;
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
