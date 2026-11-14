using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
    // 常駐アプリとして数日〜数週間起動しっぱなしになりうるため、起動時1回だけでなく定期的に
    // 再確認する。GitHub APIの無認証レート制限(60回/時)に対して十分小さい頻度。
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    // 自己アップデート(約75MBのダウンロードと置き換え)全体のタイムアウト。
    private static readonly TimeSpan SelfUpdateTimeout = TimeSpan.FromMinutes(10);

    private TaskbarIcon? _trayIcon;
    private RecruitmentMonitorService? _monitor;
    private NotificationWindow? _notificationWindow;

    // タグ編集ウィンドウ(Issue #32)。同時に2つ開けないよう、開いている間はこのフィールドで
    // 参照を保持し、再度「タグを編集」が押されたら新規に開かず既存のウィンドウを前面に出す。
    private TagEditWindow? _tagEditWindow;
    private AppSettings _settings = AppSettings.Default;
    private System.Threading.Timer? _updateCheckTimer;
    private MenuItem? _versionMenuItem;
    private Run? _versionRun;
    private Run? _updateBadgeRun;
    private UpdateCheckService.UpdateInfo? _availableUpdate;
    private bool _isUpdating;

    // Windowsの標準アクセントカラー。更新可能であることを、警告色(赤)ほど強くなく、
    // かといって無効化されたように見えるグレーでもない色で示すため。
    private static readonly Brush UpdateAvailableBrush =
        new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

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

        // 監視サービスの初期化とは無関係な処理のため、独立してバックグラウンドで実行する。
        // 起動時に即座に1回実行し(dueTime: Zero)、以後UpdateCheckIntervalおきに再確認する。
        _updateCheckTimer = new System.Threading.Timer(
            _ => Dispatcher.BeginInvoke(new Action(async () => await CheckForUpdateAsync()), DispatcherPriority.Background),
            null,
            TimeSpan.Zero,
            UpdateCheckInterval);
    }

    private static Version GetCurrentVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    // アセンブリのバージョンは常に4桁(例: 0.1.9.0)だが、GitHubのリリースタグ表記
    // (例: v0.1.9)に合わせて3桁+"v"接頭辞の形で表示する。
    private static string FormatVersion(Version version) => $"v{version.Major}.{version.Minor}.{version.Build}";

    /// <summary>
    /// GitHub Releasesの最新版を確認し、実行中のバージョンより新しければコンテキストメニューの
    /// バージョン表示欄に反映する(Issue #27)。サーバーとの通信を伴う機能ではなく強制更新の
    /// 必要も無いため、モーダルダイアログのような割り込みはせず、メニューを開いたときに
    /// 気づける程度の控えめな見せ方にしている。オフライン・API制限等で確認自体に失敗しても、
    /// ログに残すだけでアプリの動作は妨げない。
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            var update = await UpdateCheckService.CheckForUpdateAsync(currentVersion);
            if (update is not null)
            {
                DiagnosticLog.Write($"[更新確認] 新しいバージョンがあります: {update.LatestVersion}");
            }

            ApplyVersionMenuState(currentVersion, update);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[更新確認] 確認に失敗しました(次回の定期確認まで待機): {ex}");
        }
    }

    /// <summary>
    /// バージョン表示メニュー項目を、確認結果に応じた表示に切り替える。
    /// 呼び出し元(CheckForUpdateAsync)はDispatcher.BeginInvoke経由でUIスレッド上で実行される
    /// ため、ここでUI要素を直接操作してよい。
    /// </summary>
    private void ApplyVersionMenuState(Version currentVersion, UpdateCheckService.UpdateInfo? update)
    {
        if (_versionRun is null || _updateBadgeRun is null || _versionMenuItem is null)
        {
            return;
        }

        _versionRun.Text = FormatVersion(currentVersion);

        if (update is null)
        {
            _updateBadgeRun.Text = "";
            _versionMenuItem.IsEnabled = false;
            _availableUpdate = null;
        }
        else
        {
            _updateBadgeRun.Text = "（更新可能）";
            _versionMenuItem.IsEnabled = true;
            _availableUpdate = update;
        }
    }

    /// <summary>
    /// バージョン表示メニュー項目のクリック処理。更新が無い間はIsEnabled=falseにしてあるため、
    /// ホバー時のハイライトやクリック自体が発生しない(項目が存在を主張しすぎないようにするため)。
    /// 更新用zipの情報が揃い、exeのフォルダに書き込める場合は、確認ダイアログのうえで自己アップデート
    /// して再起動する(<see cref="SelfUpdateService"/>)。
    /// それ以外(開発実行・書き込み不可・zip情報なし)や、自己アップデートに失敗した場合は、
    /// 従来どおりリリースページを開く。
    /// </summary>
    private async void OnVersionMenuItemClick(object sender, RoutedEventArgs e)
    {
        var update = _availableUpdate;
        if (update is null || _isUpdating)
        {
            return;
        }

        try
        {
            // 置き換え後は実行中のプロセスのモジュールのパスが.oldへ変わるため、置き換え前に取得する。
            var exePath = Environment.ProcessPath;
            if (!SelfUpdateService.CanSelfUpdate(update, exePath))
            {
                OpenReleasePage(update.HtmlUrl);
                return;
            }

            // 確認ダイアログの表示中にメニューから再度クリックされてダイアログが二重に出ないよう、
            // ダイアログを出す前から更新中として扱う(「いいえ」の場合は戻す)。
            _isUpdating = true;

            var sizeText = update.AssetSizeBytes is { } bytes ? $"約{bytes / 1024 / 1024}MB" : "ダウンロード";
            var confirm = await ShowMessageBoxAsync(
                $"{FormatVersion(update.LatestVersion)}に更新します。{sizeText}をダウンロードして、自動的に再起動します。\n\nよろしいですか？",
                "更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                _isUpdating = false;
                return;
            }

            _versionMenuItem!.IsEnabled = false;
            _updateBadgeRun!.Text = "（更新中…）";
            DiagnosticLog.Write($"[自己更新] {FormatVersion(update.LatestVersion)}への更新を開始: {exePath}");

            try
            {
                // 通信が止まって「更新中…」のまま戻らなくならないよう、全体にタイムアウトをかける。
                using var timeout = new CancellationTokenSource(SelfUpdateTimeout);
                await SelfUpdateService.ApplyUpdateAsync(update, exePath!, timeout.Token);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"[自己更新] 失敗: {ex}");
                var openPage = await ShowMessageBoxAsync(
                    $"自動更新に失敗しました。リリースページを開きますか？\n\n{ex.Message}",
                    "更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (openPage == MessageBoxResult.Yes)
                {
                    OpenReleasePage(update.HtmlUrl);
                }

                _isUpdating = false;
                ApplyVersionMenuState(GetCurrentVersion(), update);
                return;
            }

            DiagnosticLog.Write("[自己更新] 置き換え完了、再起動します");
            await RestartApplicationAsync(exePath, "更新");
        }
        catch (Exception ex)
        {
            // async voidのため、ここで握りつぶさないとアプリ全体がクラッシュする。
            DiagnosticLog.Write($"[自己更新] 想定外のエラー: {ex}");
            _isUpdating = false;
        }
    }

    private static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[更新確認] リリースページを開けませんでした: {ex}");
        }
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
        menu.Items.Add(BuildVersionMenuItem());

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "終了" };
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

        SaveSettings(_settings with { Locale = locale });
        SetCheckedLanguage(locale);

        var restartNow = await ShowMessageBoxAsync(
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

    /// <summary>
    /// 現在のバージョンを表示するメニュー項目。バージョン文字列と、更新可能なときだけ添える
    /// 括弧書きのバッジ("（更新可能）")を1つのTextBlock(Run2つ)にまとめてHeaderに使う。
    /// 更新が無い間はIsEnabled=falseにして無効化する(ホバー・クリックの見た目を出さないため)。
    /// CheckForUpdateAsyncの確認結果に応じた表示更新はApplyVersionMenuStateが後から行う
    /// (_versionRun/_updateBadgeRunを保持)。
    /// </summary>
    private MenuItem BuildVersionMenuItem()
    {
        // 右寄せのバッジ列(Grid)にしようとしたが、既定のMenuItemテンプレートがヘッダーの
        // ContentPresenterを内容に合わせた幅にしてしまい、列を分けても右端まで伸びなかった
        // (テンプレートを丸ごと差し替えるほどの手間には見合わないため断念)。素直に
        // バージョン文字列の直後に括弧書きで添える形に戻し、色分けだけ残す。
        _versionRun = new Run(FormatVersion(GetCurrentVersion()));
        _updateBadgeRun = new Run
        {
            Foreground = UpdateAvailableBrush,
            FontWeight = FontWeights.Bold,
        };

        var versionText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        // MenuItem.IsEnabled=falseの既定スタイルは前景色を薄いグレーに差し替えるが、DynamicResource
        // 参照もローカル値と同じ優先度で扱われるため、これを設定しておけば無効化時にも通常の文字色の
        // まま表示される。SystemColors.MenuTextBrush(静的スナップショット)ではなくキー経由の
        // DynamicResourceにしているのは、アプリ起動中にOSのテーマ(ライト/ダーク等)が変わっても
        // 他のメニュー項目と同様に追従させるため。
        versionText.SetResourceReference(TextBlock.ForegroundProperty, System.Windows.SystemColors.MenuTextBrushKey);
        versionText.Inlines.Add(_versionRun);
        versionText.Inlines.Add(_updateBadgeRun);

        _versionMenuItem = new MenuItem
        {
            Header = versionText,
            IsEnabled = false,
        };
        _versionMenuItem.Click += OnVersionMenuItemClick;
        return _versionMenuItem;
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
            await ShowMessageBoxAsync(
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
        _updateCheckTimer?.Dispose();
        _monitor?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
