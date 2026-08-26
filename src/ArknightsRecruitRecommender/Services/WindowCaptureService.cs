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
/// アークナイツのゲームウィンドウから Windows.Graphics.Capture を使ってフレームを取得する。
/// ゲームはDirectX/GPU描画のため、古典的なBitBlt/PrintWindowでは正しく読み取れず、このAPIが必須となる。
///
/// キャプチャセッション(GraphicsCaptureItem/Direct3D11CaptureFramePool/GraphicsCaptureSession)は
/// 構築コストが高いため、<see cref="EnsureSessionStarted"/>で開始したセッションを使い回し、
/// <see cref="TryGetLatestFrameAsync"/>で最新フレームを取得するだけにしている。これにより
/// 1秒間隔のような高頻度なポーリングでもオーバーヘッドを抑えられる。
///
/// このクラス自体はスレッドセーフではない。呼び出し元(RecruitmentMonitorService)が
/// 同時に複数スレッドから呼び出さないよう責任を持つ。
/// </summary>
public sealed class WindowCaptureService : IDisposable
{
    private readonly IntPtr _d3d11Device;
    private readonly IDirect3DDevice _direct3DDevice;

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IntPtr _activeHwnd;
    private Windows.Graphics.SizeInt32 _framePoolSize;

    public WindowCaptureService()
    {
        _d3d11Device = GraphicsCaptureInterop.CreateD3D11Device();
        _direct3DDevice = GraphicsCaptureInterop.CreateDirect3DDeviceFromD3D11Device(_d3d11Device);
    }

    /// <summary>
    /// 実行中プロセスから、ウィンドウタイトルの部分一致（大文字小文字を無視）でメインウィンドウを探す。
    /// プロセス/ウィンドウの列挙のみでキャプチャを伴わない軽量な処理のため、ゲームの起動検知・
    /// 終了検知（見つからなくなったら終了とみなす）の両方に毎ティック使う。
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
    /// 指定ウィンドウ向けのキャプチャセッションを開始する（既に同じウィンドウで起動済みなら何もしない）。
    /// <see cref="TryGetLatestFrameAsync"/>を呼ぶ前に毎ティック呼び出すこと。
    /// </summary>
    public void EnsureSessionStarted(IntPtr hwnd)
    {
        if (_session is not null && _activeHwnd == hwnd)
        {
            return;
        }

        StopSession();

        _item = GraphicsCaptureInterop.CreateItemForWindow(hwnd);
        _framePoolSize = _item.Size;
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            _framePoolSize);
        _session = _framePool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
        _activeHwnd = hwnd;
    }

    /// <summary>
    /// 起動中のキャプチャセッションを破棄する（ゲームウィンドウが見つからなくなった＝終了した際に
    /// 呼び出し、GPUリソースを解放するため）。
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
    /// 起動中のセッションから最新フレームを取得する（新しいフレームの到着を待たない）。
    /// セッション未起動、またはまだフレームが1枚も生成されていない場合はnullを返す。
    ///
    /// ゲーム側でウィンドウサイズ（解像度設定）が変わると、以後届くフレームのContentSizeが
    /// フレームプール作成時のサイズと一致しなくなる。これを検知せず放置すると、キャプチャ内容が
    /// 引き伸ばされたり切れたりしたまま以後ずっと壊れるため、サイズが変わっていたらプールを
    /// 作り直す(Recreate)。
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

        if (frame.ContentSize.Width != _framePoolSize.Width || frame.ContentSize.Height != _framePoolSize.Height)
        {
            _framePoolSize = frame.ContentSize;
            _framePool.Recreate(
                _direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                numberOfBuffers: 2,
                _framePoolSize);
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
