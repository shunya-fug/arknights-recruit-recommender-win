using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// Captures still frames from the Arknights game window using Windows.Graphics.Capture.
/// This API (rather than the classic BitBlt/PrintWindow) is required because the game window is
/// rendered via DirectX/GPU compositing, which BitBlt cannot reliably read.
///
/// The capture session (GraphicsCaptureItem/Direct3D11CaptureFramePool/GraphicsCaptureSession)
/// is expensive to set up, so it is kept alive and reused across polling ticks via
/// <see cref="EnsureSessionStarted"/> / <see cref="TryGetLatestFrameAsync"/> rather than being
/// recreated every time a frame is needed. This is what makes frequent (e.g. 1 second interval)
/// polling affordable.
/// </summary>
public sealed class WindowCaptureService : IDisposable
{
    private readonly IntPtr _d3d11Device;
    private readonly IDirect3DDevice _direct3DDevice;

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IntPtr _activeHwnd;

    public WindowCaptureService()
    {
        _d3d11Device = GraphicsCaptureInterop.CreateD3D11Device();
        _direct3DDevice = GraphicsCaptureInterop.CreateDirect3DDeviceFromD3D11Device(_d3d11Device);
    }

    /// <summary>
    /// Finds the main window of a running process by (partial, case-insensitive) title match.
    /// Cheap (process/window enumeration only, no capture) - used every tick both to detect the
    /// game starting and, when it stops returning a match, to detect the game closing.
    /// </summary>
    public static IntPtr? FindWindowByTitle(string titleContains)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero &&
                    process.MainWindowTitle.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                {
                    return process.MainWindowHandle;
                }
            }
            catch
            {
                // Some processes throw when queried (access denied, exited mid-enumeration); skip them.
            }
        }

        return null;
    }

    /// <summary>
    /// Starts (or keeps, if already running for this exact window) a capture session for hwnd.
    /// Call this every tick before <see cref="TryGetLatestFrameAsync"/> - it is a no-op when the
    /// session is already active for the same window.
    /// </summary>
    public void EnsureSessionStarted(IntPtr hwnd)
    {
        if (_session is not null && _activeHwnd == hwnd)
        {
            return;
        }

        StopSession();

        _item = GraphicsCaptureInterop.CreateItemForWindow(hwnd);
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            _item.Size);
        _session = _framePool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
        _activeHwnd = hwnd;
    }

    /// <summary>
    /// Tears down the active capture session (called once the game window is no longer found,
    /// i.e. the game was closed) so GPU resources are not held while nothing is running.
    /// </summary>
    public void StopSession()
    {
        _session?.Dispose();
        _framePool?.Dispose();
        _session = null;
        _framePool = null;
        _item = null;
        _activeHwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Pulls the most recently captured frame from the active session without waiting for a new
    /// one to arrive. Returns null if no session is active, or no frame has been produced yet.
    /// </summary>
    public async Task<BitmapSource?> TryGetLatestFrameAsync()
    {
        if (_framePool is null)
        {
            return null;
        }

        using var frame = _framePool.TryGetNextFrame();
        if (frame is null)
        {
            return null;
        }

        return await ConvertFrameToBitmapSourceAsync(frame);
    }

    private static async Task<BitmapSource> ConvertFrameToBitmapSourceAsync(Direct3D11CaptureFrame frame)
    {
        var surface = frame.Surface;
        var bitmap = await Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(surface);

        var bgra = Windows.Graphics.Imaging.SoftwareBitmap.Convert(
            bitmap,
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var buffer = new byte[4 * width * height];
        bgra.CopyToBuffer(buffer.AsBuffer());

        var writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        writeableBitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), buffer, 4 * width, 0);
        writeableBitmap.Freeze();
        return writeableBitmap;
    }

    public void Dispose()
    {
        StopSession();
        Marshal.Release(_d3d11Device);
    }
}
