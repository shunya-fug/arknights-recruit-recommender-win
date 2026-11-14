using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;
using ArknightsRecruitRecommender.Views;
using H.NotifyIcon;
using Icon = System.Drawing.Icon;
using Language = Windows.Globalization.Language;

namespace ArknightsRecruitRecommender;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private RecruitmentMonitorService? _monitor;
    private NotificationWindow? _notificationWindow;

    // タグ編集ウィンドウ(Issue #32)。同時に2つ開けないよう、開いている間はこのフィールドで
    // 参照を保持し、再度「タグを編集」が押されたら新規に開かず既存のウィンドウを前面に出す。
    private TagEditWindow? _tagEditWindow;
    private AppSettings _settings = AppSettings.Default;

    // トレイメニューのバージョン表示と、更新確認・自己アップデート。
    private UpdateMenuController? _updateMenu;

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

        _updateMenu = new UpdateMenuController(RestartApplicationAsync);

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = LoadTrayIcon();
        _trayIcon.ContextMenu = BuildContextMenu();
        _trayIcon.ForceCreate();
        DiagnosticLog.Write("[起動] トレイアイコン作成完了");

        _notificationWindow = new NotificationWindow(_settings.NotificationPosition);
        _notificationWindow.GameWindowProvider = () => _monitor?.GameWindow;
        _notificationWindow.SetCompactDisplay(_settings.CompactNotificationDisplay);
        _notificationWindow.SetRecruitTimeBand(_settings.RecruitTimeBand);

        // StartMonitor()はD3D11デバイス・OCRエンジンの作成を含み、実機計測で約1秒かかる。
        // OnStartup内で同期的に呼ぶと、ディスパッチャのメッセージループが動き出すまでの間
        // Windowsが起動中カーソル(ローディング表示)を出し続けてしまう。トレイアイコンの
        // 表示を優先し、メッセージループが動き出した直後に非同期で開始することで、
        // 起動時のカーソル表示を短縮する。
        // StartupRegistration.RefreshPathIfRegistered()のレジストリ書き込みも通常は1ms未満だが、
        // ウイルス対策ソフトによるフック等で遅くなる可能性はゼロではないため、同じ理由で
        // ここに合わせて遅延させる。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            StartupRegistration.RefreshPathIfRegistered();
            // 前回の自己アップデートで残った置き換え前のexe(.old)を片付ける。古いプロセスの終了待ちで
            // 数秒かかることがあるため、完了を待たずバックグラウンドで行う(失敗しても動作に影響しない)。
            _ = SelfUpdateService.CleanupLeftoversAsync(Environment.ProcessPath);
            StartMonitor();
            DiagnosticLog.Write("[起動] 監視サービス初期化完了(D3D11デバイス・OCRエンジン作成を含む)");
        }), DispatcherPriority.Background);

        _updateMenu.Start();
    }

    /// <summary>
    /// 常時監視は常に動作する。「手動チェック実行」メニューはそれとは独立して、いつでも手動で
    /// 1回分のチェックを実行し、結果をdebug-output/に書き出すための機能。
    /// 言語設定を切り替えた際は、アプリ再起動によってこのメソッドが新しいロケールで
    /// 再度呼ばれる想定(実行中のインスタンスをホットスワップはしない)。
    /// </summary>
    private void StartMonitor()
    {
        _monitor = new RecruitmentMonitorService(_settings.Locale, _settings.RecruitTimeBand);
        _monitor.RecommendationsUpdated += (matchedTags, results) =>
            Dispatcher.Invoke(() => _notificationWindow!.ShowResults(matchedTags, results));
        _monitor.RecruitmentScreenLost += () =>
            Dispatcher.Invoke(() => _notificationWindow!.HideNotification());
        // EditTagsButton_Clickの延長でUIスレッドから直接発火するため、RecommendationsUpdated等と
        // 違いDispatcher.Invokeは不要。
        _notificationWindow!.TagEditRequested += OnTagEditRequested;
        _notificationWindow.RecruitTimeBandChanged += OnRecruitTimeBandChanged;
        _notificationWindow.BandHintProvider = (tags, band) => _monitor!.FindRarityGainInLongerBand(tags, band);
    }

    /// <summary>
    /// 想定する募集時間の区間が切り替えられた(Issue #34)。設定に保存し、常時監視の判定に反映する。
    /// 自動モードでは次のポーリングで新しい区間の結果が通知される。手動モード中は監視の結果が
    /// 表示に反映されず、手動チェックの結果も固定のため、ここで表示中のタグを新しい区間で再判定して反映する。
    /// </summary>
    private void OnRecruitTimeBandChanged(RecruitTimeBand band)
    {
        SaveSettings(_settings with { RecruitTimeBand = band });
        _monitor!.RecruitTimeBand = band;

        if (_notificationWindow!.HasStaticResults)
        {
            _notificationWindow.RefreshStaticResults(_monitor.EvaluateTagsForDisplay(_notificationWindow.DisplayedTags));
        }
    }

    /// <summary>
    /// 「タグを編集」(Issue #32)でタグ編集ウィンドウを開く。既に開いている場合は新規に
    /// 開かず前面に出す(1つのアプリにつき通知ウィンドウは1つなので、編集対象が競合しないため
    /// 複数同時に開く必要が無い)。
    /// </summary>
    private void OnTagEditRequested(IReadOnlyList<string> tagsAtOpen)
    {
        if (_tagEditWindow is not null)
        {
            _tagEditWindow.Activate();
            return;
        }

        _tagEditWindow = new TagEditWindow(_monitor!.KnownTags, tagsAtOpen);
        _tagEditWindow.Confirmed += selectedTags =>
        {
            _notificationWindow!.ApplyTagEditResult(tagsAtOpen, selectedTags, _monitor!.EvaluateTagsForDisplay(selectedTags));
        };
        _tagEditWindow.Closed += (_, _) => _tagEditWindow = null;
        _tagEditWindow.Show();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var manualCheckItem = new MenuItem { Header = "手動チェック実行" };
        manualCheckItem.Click += async (_, _) => await RunManualCheckAsync();
        menu.Items.Add(manualCheckItem);

        menu.Items.Add(BuildLanguageMenuItem());
        menu.Items.Add(BuildNotificationPositionMenuItem());
        menu.Items.Add(BuildCompactDisplayMenuItem());
        var startupItem = BuildStartupMenuItem();
        menu.Items.Add(startupItem);
        // レジストリの値そのものを正とする(AppSettingsに独自の状態を持たない)ため、
        // タスクマネージャーのスタートアップタブ等から直接変更された場合にもズレないよう、
        // メニューを開く直前に毎回チェック状態を読み直す。読み取り自体は高速(1ms未満)。
        menu.Opened += (_, _) => startupItem.IsChecked = StartupRegistration.IsRegistered();

        menu.Items.Add(new Separator());
        menu.Items.Add(_updateMenu!.MenuItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "終了" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>設定を更新し、すぐにファイルへ保存する(トレイメニュー・通知ウィンドウからの変更用)。</summary>
    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        AppSettingsStore.Save(settings);
    }

    // MenuItemのIsCheckableはラジオボタンのような排他選択を自動ではしてくれない
    // (クリックされた項目自身のIsCheckedがトグルされるだけ)ため、兄弟項目のチェック状態は
    // このリストを使って手動で管理する。
    private readonly List<(string Locale, MenuItem Item)> _languageMenuItems = new();

    /// <summary>
    /// 「言語」サブメニュー。選択肢はData/operators.{locale}.jsonの存在から動的に決まる
    /// (新しい言語のデータファイルを追加するだけで選択肢に反映される)。
    /// </summary>
    private MenuItem BuildLanguageMenuItem()
    {
        var languageMenu = new MenuItem { Header = "言語" };
        _languageMenuItems.Clear();

        foreach (var locale in OperatorDataProvider.GetAvailableLocales())
        {
            var language = new Language(locale);
            var item = new MenuItem
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
            var proceed = await TrayDialogs.ShowMessageBoxAsync(
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

        SaveSettings(_settings with { Locale = locale });
        SetCheckedLanguage(locale);

        var restartNow = await TrayDialogs.ShowMessageBoxAsync(
            "言語設定を変更しました。反映するにはアプリの再起動が必要です。今すぐ再起動しますか？",
            "言語設定",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (restartNow == MessageBoxResult.Yes)
        {
            await RestartApplicationAsync(Environment.ProcessPath, "言語設定");
        }
    }

    private void SetCheckedLanguage(string locale)
    {
        foreach (var (itemLocale, item) in _languageMenuItems)
        {
            item.IsChecked = itemLocale == locale;
        }
    }

    private readonly List<(NotificationPosition Position, MenuItem Item)> _positionMenuItems = new();

    /// <summary>
    /// 「通知の表示位置」サブメニュー。言語設定と異なりOCR等の再初期化を伴わないため、
    /// 選択直後に表示中の通知ウィンドウへ即時反映し、アプリ再起動は不要にする(Issue #11)。
    /// </summary>
    private MenuItem BuildNotificationPositionMenuItem()
    {
        var positionMenu = new MenuItem { Header = "通知の表示位置" };
        _positionMenuItems.Clear();

        foreach (var (position, label) in NotificationPositionLabels.Options)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = position == _settings.NotificationPosition,
            };
            item.Click += (_, _) => OnNotificationPositionSelected(position);
            _positionMenuItems.Add((position, item));
            positionMenu.Items.Add(item);
        }

        return positionMenu;
    }

    private void OnNotificationPositionSelected(NotificationPosition position)
    {
        if (position == _settings.NotificationPosition)
        {
            return;
        }

        SaveSettings(_settings with { NotificationPosition = position });

        foreach (var (itemPosition, item) in _positionMenuItems)
        {
            item.IsChecked = itemPosition == position;
        }

        _notificationWindow?.SetPosition(position);
    }

    /// <summary>
    /// 「通知をコンパクト表示」トグル。位置設定と同様、選択直後に表示中の通知ウィンドウへ
    /// 即時反映し、アプリ再起動は不要にする。
    /// </summary>
    private MenuItem BuildCompactDisplayMenuItem()
    {
        var item = new MenuItem
        {
            Header = "通知をコンパクト表示",
            IsCheckable = true,
            IsChecked = _settings.CompactNotificationDisplay,
        };
        // IsCheckable=trueのMenuItemはクリック時にIsCheckedが自動的にトグルされた「後」で
        // Clickイベントが発火するため、ここでの値をそのまま新しい設定値として使える。
        item.Click += (_, _) => OnCompactDisplayToggled(item.IsChecked);
        return item;
    }

    private void OnCompactDisplayToggled(bool isChecked)
    {
        SaveSettings(_settings with { CompactNotificationDisplay = isChecked });
        _notificationWindow?.SetCompactDisplay(isChecked);
    }

    /// <summary>
    /// 「Windows起動時に自動起動」トグル。AppSettingsには独自の状態を持たず、
    /// レジストリ(HKCU\...\Run)の実際の値そのものを正とする(<see cref="StartupRegistration"/>参照)。
    /// </summary>
    private MenuItem BuildStartupMenuItem()
    {
        var item = new MenuItem
        {
            Header = "Windows起動時に自動起動",
            IsCheckable = true,
            IsChecked = StartupRegistration.IsRegistered(),
        };
        item.Click += (_, _) => StartupRegistration.SetRegistered(item.IsChecked);
        return item;
    }

    /// <param name="exePath">
    /// 再起動するexeのパス。自己アップデート後は、プロセスのモジュールのパスが置き換え前のexe(.old)を
    /// 指すため、置き換え前に取得した値を渡す。それ以外では<see cref="Environment.ProcessPath"/>でよい。
    /// </param>
    /// <param name="caption">失敗時のダイアログのタイトル。</param>
    private async Task RestartApplicationAsync(string? exePath, string caption)
    {
        try
        {
            if (exePath is not null)
            {
                Process.Start(exePath);
            }
        }
        catch (Exception ex)
        {
            // 自動再起動に失敗しても、これから終了すること自体は変わらない。手動での
            // 再起動をお願いするだけにとどめ、原因不明のクラッシュとして落とさない。
            await TrayDialogs.ShowMessageBoxAsync(
                $"アプリの自動再起動に失敗しました。手動で起動し直してください。\n\n{ex.Message}",
                caption,
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
                await TrayDialogs.ShowMessageBoxAsync(
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
            await TrayDialogs.ShowMessageBoxAsync(
                $"手動チェック実行中にエラーが発生しました:\n{ex}",
                "手動チェック実行",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Assets/AppIcon.icoを埋め込みリソース(LogicalName="AppIcon.ico")として読み込む。
    /// 実行ファイル横のファイルパスに依存する方式(AppContext.BaseDirectory等)は、
    /// dotnet publish(単一ファイル発行)時にApplicationIcon指定ファイルが出力先へコピー
    /// されず実機でDirectoryNotFoundExceptionにより起動直後にクラッシュする不具合が
    /// あったため、起動方法・発行方法によらず確実に読めるアセンブリ埋め込みリソースに変更した。
    /// </summary>
    private static Icon LoadTrayIcon()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("AppIcon.ico")
            ?? throw new InvalidOperationException("埋め込みリソース AppIcon.ico が見つかりません。");
        return new Icon(stream);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateMenu?.Dispose();
        _monitor?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
