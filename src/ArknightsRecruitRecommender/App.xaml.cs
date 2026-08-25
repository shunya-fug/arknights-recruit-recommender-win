using System.Drawing;
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

        _monitor = new RecruitmentMonitorService(new OperatorDataProvider());
        _monitor.GoodCombinationsFound += results =>
            Dispatcher.Invoke(() => _notificationWindow.ShowResults(results));
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
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
