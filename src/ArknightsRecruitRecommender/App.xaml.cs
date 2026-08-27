using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;
using ArknightsRecruitRecommender.Views;
using H.NotifyIcon;
using Language = Windows.Globalization.Language;

namespace ArknightsRecruitRecommender;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private RecruitmentMonitorService? _monitor;
    private NotificationWindow? _notificationWindow;
    private AppSettings _settings = AppSettings.Default;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 実機での動作確認時、GUIのダイアログ/通知ウィンドウが一瞬で消えて内容を確認できない
        // ケースがあったため、UIスレッドで捕捉されなかった例外は握りつぶさずログに残す。
        DispatcherUnhandledException += (_, args) =>
        {
            DiagnosticLog.Write($"[UnhandledException] {args.Exception}");
#if DEBUG
            // 実機検証中は、原因調査のためにアプリ全体を落とさずログだけ残して続行する。
            // Releaseビルドでは適用しない(未知の例外を恒久的に握りつぶしたまま配布しないため)。
            args.Handled = true;
#endif
        };

        DiagnosticLog.Write("=== アプリ起動 ===");

        _settings = AppSettingsStore.Load();

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = CreatePlaceholderIcon();
        _trayIcon.ContextMenu = BuildContextMenu();
        _trayIcon.ForceCreate();

        _notificationWindow = new NotificationWindow();

        StartMonitor();
    }

    /// <summary>
    /// 常時監視は常に動作する。「手動チェック実行」メニューはそれとは独立して、いつでも手動で
    /// 1回分のチェックを実行し、結果をdebug-output/に書き出すための機能。
    /// 言語設定を切り替えた際は、アプリ再起動によってこのメソッドが新しいロケールで
    /// 再度呼ばれる想定(実行中のインスタンスをホットスワップはしない)。
    /// </summary>
    private void StartMonitor()
    {
        _monitor = new RecruitmentMonitorService(_settings.Locale);
        _monitor.GoodCombinationsFound += results =>
            Dispatcher.Invoke(() => _notificationWindow!.ShowResults(results));
        _monitor.RecruitmentScreenLost += () =>
            Dispatcher.Invoke(() => _notificationWindow!.Hide());
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var manualCheckItem = new System.Windows.Controls.MenuItem { Header = "手動チェック実行" };
        manualCheckItem.Click += async (_, _) => await RunManualCheckAsync();
        menu.Items.Add(manualCheckItem);

        menu.Items.Add(BuildLanguageMenuItem());

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// トレイのコンテキストメニュー項目のクリック直後にMessageBox.Showを呼ぶと、メニューを
    /// 閉じたクリックの入力メッセージがそのまま新しいダイアログに流れ込み、表示直後に自動的に
    /// 閉じてしまう不具合を実機で確認した(表示から149ms後に閉じており、人がクリックできる
    /// 速さではなかった)。入力キューが落ち着くまで少し待ってから表示することで回避する。
    /// メニュー項目のクリックハンドラから表示するダイアログは、必ずこれ経由で表示すること。
    /// </summary>
    private static async Task<MessageBoxResult> ShowMessageBoxAsync(
        string text, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        await Task.Delay(200);
        return MessageBox.Show(text, caption, button, icon);
    }

    // MenuItemのIsCheckableはラジオボタンのような排他選択を自動ではしてくれない
    // (クリックされた項目自身のIsCheckedがトグルされるだけ)ため、兄弟項目のチェック状態は
    // このリストを使って手動で管理する。
    private readonly List<(string Locale, System.Windows.Controls.MenuItem Item)> _languageMenuItems = new();

    /// <summary>
    /// 「言語」サブメニュー。選択肢はData/operators.{locale}.jsonの存在から動的に決まる
    /// (新しい言語のデータファイルを追加するだけで選択肢に反映される)。
    /// </summary>
    private System.Windows.Controls.MenuItem BuildLanguageMenuItem()
    {
        var languageMenu = new System.Windows.Controls.MenuItem { Header = "言語" };
        _languageMenuItems.Clear();

        foreach (var locale in OperatorDataProvider.GetAvailableLocales())
        {
            var language = new Language(locale);
            var item = new System.Windows.Controls.MenuItem
            {
                Header = language.DisplayName,
                IsCheckable = true,
                IsChecked = locale == _settings.Locale,
            };
            item.Click += async (_, _) => await OnLanguageSelectedAsync(locale);
            _languageMenuItems.Add((locale, item));
            languageMenu.Items.Add(item);
        }

        return languageMenu;
    }

    private async Task OnLanguageSelectedAsync(string locale)
    {
        if (locale == _settings.Locale)
        {
            SetCheckedLanguage(_settings.Locale);
            return;
        }

        var language = new Language(locale);
        if (!TagOcrService.IsLanguageAvailable(language))
        {
            var proceed = await ShowMessageBoxAsync(
                $"「{language.DisplayName}」のOCR言語パックがこの端末にインストールされていません。" +
                "このまま切り替えても、パックを追加するまで画面のタグを認識できません。\n\n" +
                "切り替えを続けますか？（設定 > 時刻と言語 > 言語と地域 から追加できます）",
                "言語設定",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (proceed != MessageBoxResult.Yes)
            {
                SetCheckedLanguage(_settings.Locale);
                return;
            }
        }

        _settings = _settings with { Locale = locale };
        AppSettingsStore.Save(_settings);
        SetCheckedLanguage(locale);

        var restartNow = await ShowMessageBoxAsync(
            "言語設定を変更しました。反映するにはアプリの再起動が必要です。今すぐ再起動しますか？",
            "言語設定",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (restartNow == MessageBoxResult.Yes)
        {
            await RestartApplicationAsync();
        }
    }

    private void SetCheckedLanguage(string locale)
    {
        foreach (var (itemLocale, item) in _languageMenuItems)
        {
            item.IsChecked = itemLocale == locale;
        }
    }

    private async Task RestartApplicationAsync()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath is not null)
            {
                Process.Start(exePath);
            }
        }
        catch (Exception ex)
        {
            // 自動再起動に失敗しても、これから終了すること自体は変わらない。手動での
            // 再起動をお願いするだけにとどめ、原因不明のクラッシュとして落とさない。
            await ShowMessageBoxAsync(
                $"アプリの自動再起動に失敗しました。手動で起動し直してください。\n\n{ex.Message}",
                "言語設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Shutdown();
    }

    /// <summary>
    /// トレイメニューから呼ばれる、手動・単発のキャプチャ→OCR→判定処理。起動方法によらず常に
    /// 利用可能。常時監視のポーリングと異なり、結果が無い場合（タグ未検出）も含めて必ず表示する。
    /// Debugビルド限定でキャプチャ画像とOCR結果をdebug-output/に書き出す
    /// （Release配布ビルドでは通常ユーザーの環境に余計なファイルを残さない）。
    /// </summary>
    private async Task RunManualCheckAsync()
    {
        if (_monitor is null || _notificationWindow is null)
        {
            return;
        }

        DiagnosticLog.Write("[手動チェック] 開始");
        try
        {
            var result = await _monitor.CheckOnceAsync();
            DiagnosticLog.Write($"[手動チェック] CheckOnceAsync完了: result={(result is null ? "null" : "非null")}");
            if (result is null)
            {
                await ShowMessageBoxAsync(
                    "アークナイツの画面を取得できませんでした。ゲームが起動しているか、" +
                    "ウィンドウが最小化されていないか確認してください。",
                    "手動チェック実行",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DiagnosticLog.Write(
                $"[手動チェック] 検出タグ: {(result.MatchedTags.Count == 0 ? "(一致なし)" : string.Join(" / ", result.MatchedTags))}");

#if DEBUG
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "debug-output");
            DebugArtifactWriter.Write(result, outputDirectory);
            DiagnosticLog.Write($"[手動チェック] debug-output書き出し完了: {outputDirectory}");
#endif

            _notificationWindow.ShowDebugResult(result);
            DiagnosticLog.Write("[手動チェック] 通知ウィンドウ表示呼び出し完了");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[手動チェック] 例外発生: {ex}");
            await ShowMessageBoxAsync(
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
