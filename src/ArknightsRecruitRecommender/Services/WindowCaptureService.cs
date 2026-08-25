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
/// Captures a single still frame from the Arknights game window using Windows.Graphics.Capture.
/// This API (rather than the classic BitBlt/PrintWindow) is required because the game window is
/// rendered via DirectX/GPU compositing, which BitBlt cannot reliably read.
/// </summary>
public sealed class WindowCaptureService : IDisposable
{
    private readonly IntPtr _d3d11Device;
    private readonly IDirect3DDevice _direct3DDevice;

    public WindowCaptureService()
    {
        _d3d11Device = GraphicsCaptureInterop.CreateD3D11Device();
        _direct3DDevice = GraphicsCaptureInterop.CreateDirect3DDeviceFromD3D11Device(_d3d11Device);
    }

    /// <summary>
    /// Finds the main window of a running process by (partial, case-insensitive) title match.
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

    public async Task<BitmapSource> CaptureFrameAsync(IntPtr hwnd)
    {
        var item = GraphicsCaptureInterop.CreateItemForWindow(hwnd);
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 1,
            item.Size);

        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
        framePool.FrameArrived += (pool, _) =>
        {
            var frame = pool.TryGetNextFrame();
            if (frame is not null)
            {
                tcs.TrySetResult(frame);
            }
        };

        using var session = framePool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        session.StartCapture();

        using var frame = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
        Marshal.Release(_d3d11Device);
    }
}
