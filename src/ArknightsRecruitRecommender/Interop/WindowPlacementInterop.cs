using System.Runtime.InteropServices;
using System.Windows;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Interop;

/// <summary>
/// 別のウィンドウ(アークナイツのゲームウィンドウ)が載っているモニタと仮想デスクトップを調べ、
/// 通知ウィンドウをそこへ寄せるためのWin32/COM呼び出し。
/// いずれも失敗しても通知の表示自体には影響させず、呼び出し側は「今までどおりの場所に出す」へ
/// フォールバックできるようにしている。UIスレッド(STA)から呼ぶこと。
/// </summary>
internal static class WindowPlacementInterop
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // Windows 10/11が公開している仮想デスクトップ操作のCOMインターフェース(IVirtualDesktopManager)。
    // メソッドの並び順がそのままvtableの順序になるため、使わないメソッドも省略しないこと。
    // 他プロセスのウィンドウについては、所属デスクトップの取得(GetWindowDesktopId)だけが許可されていて、
    // 移動(MoveWindowToDesktop)は呼び出したプロセス自身が作ったウィンドウにしか使えない。
    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [ComImport]
    [Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManagerClass
    {
    }

    // 通知を出すたびに作り直さないよう使い回す。例外が出たら破棄して、次回作り直す。
    private static IVirtualDesktopManager? _manager;

    // 失敗が続く環境でログが溢れないよう、同じ内容のログは1回だけ記録する。
    private static readonly HashSet<string> LoggedFailures = new();

    /// <summary>
    /// 指定ウィンドウが載っているモニタの作業領域(タスクバーを除いた範囲)を、デバイスピクセルで返す。
    /// 取得できなければnull。
    /// </summary>
    public static Rect? TryGetMonitorWorkArea(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var work = info.Work;
        return new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
    }

    /// <summary>
    /// 自プロセスのウィンドウ<paramref name="ownHwnd"/>を、<paramref name="referenceHwnd"/>と同じ
    /// 仮想デスクトップへ移す。既に同じデスクトップなら何もしない。取得・移動に失敗した場合
    /// (仮想デスクトップ非対応の環境、ウィンドウが全デスクトップ固定の場合など)は何もしない。
    /// </summary>
    public static void MoveToSameDesktop(IntPtr ownHwnd, IntPtr referenceHwnd) =>
        MoveToDesktopOf(ownHwnd, referenceHwnd, "ゲームのウィンドウ");

    /// <summary>
    /// 自プロセスのウィンドウを、いま操作中の(最前面のウィンドウがある)仮想デスクトップへ移す。
    /// ゲームのウィンドウが見つからない時に、以前ゲームを追って移った先のデスクトップへ通知が
    /// 取り残されて見えなくなるのを防ぐ。既に現在のデスクトップにあるなら何もしない。
    /// </summary>
    public static void MoveToCurrentDesktop(IntPtr ownHwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var manager = GetManager();
            if (manager.IsWindowOnCurrentVirtualDesktop(ownHwnd, out var onCurrent) == 0 && onCurrent != 0)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            ResetManager(ex);
            return;
        }

        MoveToDesktopOf(ownHwnd, foreground, "最前面のウィンドウ");
    }

    private static void MoveToDesktopOf(IntPtr ownHwnd, IntPtr referenceHwnd, string referenceName)
    {
        try
        {
            var manager = GetManager();

            var hr = manager.GetWindowDesktopId(referenceHwnd, out var targetId);
            if (hr != 0 || targetId == Guid.Empty)
            {
                LogFailureOnce($"{referenceName}のデスクトップを取得できません(HRESULT=0x{hr:X8})");
                return;
            }

            if (manager.GetWindowDesktopId(ownHwnd, out var ownId) == 0 && ownId == targetId)
            {
                return;
            }

            hr = manager.MoveWindowToDesktop(ownHwnd, ref targetId);
            if (hr != 0)
            {
                LogFailureOnce($"通知ウィンドウを移動できません(HRESULT=0x{hr:X8})");
            }
        }
        catch (Exception ex)
        {
            ResetManager(ex);
        }
    }

    private static IVirtualDesktopManager GetManager() =>
        _manager ??= (IVirtualDesktopManager)new VirtualDesktopManagerClass();

    private static void ResetManager(Exception ex)
    {
        _manager = null;
        LogFailureOnce($"例外: {ex.Message}");
    }

    private static void LogFailureOnce(string detail)
    {
        if (LoggedFailures.Add(detail))
        {
            DiagnosticLog.Write($"[WindowPlacement] 仮想デスクトップの移動に失敗しました: {detail}");
        }
    }
}
