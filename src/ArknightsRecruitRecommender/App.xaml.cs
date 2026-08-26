using System.Drawing;
using System.IO;
using System.Windows;
using ArknightsRecruitRecommender.Services;
using ArknightsRecruitRecommender.Views;
using H.NotifyIcon;

namespace ArknightsRecruitRecommender;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private RecruitmentMonitorService? _monitor;
    private NotificationWindow? _notificationWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = CreatePlaceholderIcon();
        _trayIcon.ContextMenu = BuildContextMenu();
        _trayIcon.ForceCreate();

        _notificationWindow = new NotificationWindow();

        // 常時監視は常に動作する。「手動チェック実行」メニューはそれとは独立して、いつでも手動で
        // 1回分のチェックを実行し、結果をdebug-output/に書き出すための機能。
        _monitor = new RecruitmentMonitorService(new OperatorDataProvider());
        _monitor.GoodCombinationsFound += results =>
            Dispatcher.Invoke(() => _notificationWindow.ShowResults(results));
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var manualCheckItem = new System.Windows.Controls.MenuItem { Header = "手動チェック実行" };
        manualCheckItem.Click += async (_, _) => await RunManualCheckAsync();
        menu.Items.Add(manualCheckItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Manual, on-demand capture -> OCR -> analysis pass triggered from the tray menu. Always
    /// available regardless of how the app was launched. Unlike the background poll timer, this
    /// always shows a result (even "no tags detected"). Debug-build限定でキャプチャ画像とOCR結果を
    /// disk-output/に書き出す(Release配布ビルドでは通常ユーザーの環境に余計なファイルを残さない)。
    /// </summary>
    private async Task RunManualCheckAsync()
    {
        if (_monitor is null || _notificationWindow is null)
        {
            return;
        }

        try
        {
            var result = await _monitor.CheckOnceAsync();
            if (result is null)
            {
                MessageBox.Show(
                    "アークナイツの画面を取得できませんでした。ゲームが起動しているか、" +
                    "ウィンドウが最小化されていないか確認してください。",
                    "手動チェック実行",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

#if DEBUG
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "debug-output");
            DebugArtifactWriter.Write(result, outputDirectory);
#endif

            _notificationWindow.ShowDebugResult(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"手動チェック実行中にエラーが発生しました:\n{ex}",
                "手動チェック実行",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Generates a simple placeholder tray icon at runtime so the app doesn't need to ship a
    /// binary .ico asset. Replace with a real .ico via TaskbarIcon.IconSource if desired.
    /// </summary>
    private static Icon CreatePlaceholderIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(255, 79, 195, 247));
            g.FillEllipse(brush, 2, 2, 28, 28);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
