using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Views;

/// <summary>
/// トレイメニューのバージョン表示項目と、更新確認・自己アップデートの流れ(Issue #27)。
/// 定期的にGitHub Releasesの最新版を確認し、新しければ項目に「（更新可能）」を出す。項目の
/// クリックで、確認ダイアログのうえで自己アップデートして再起動する(<see cref="SelfUpdateService"/>)。
/// App.xaml.csから、更新関連の状態(見つかった更新・更新中かどうか・タイマー)ごと切り出している。
/// </summary>
internal sealed class UpdateMenuController : IDisposable
{
    // 常駐アプリとして数日〜数週間起動しっぱなしになりうるため、起動時1回だけでなく定期的に
    // 再確認する。GitHub APIの無認証レート制限(60回/時)に対して十分小さい頻度。
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    // 自己アップデート(約75MBのダウンロードと置き換え)全体のタイムアウト。
    private static readonly TimeSpan SelfUpdateTimeout = TimeSpan.FromMinutes(10);

    // Windowsの標準アクセントカラー。更新可能であることを、警告色(赤)ほど強くなく、
    // かといって無効化されたように見えるグレーでもない色で示すため。
    private static readonly Brush UpdateAvailableBrush =
        new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

    private readonly Func<string?, string, Task> _restartApplication;
    private readonly Run _versionRun;
    private readonly Run _updateBadgeRun;
    private System.Threading.Timer? _updateCheckTimer;
    private UpdateCheckService.UpdateInfo? _availableUpdate;
    private bool _isUpdating;

    /// <param name="restartApplication">
    /// 更新後にアプリを再起動する処理(引数は再起動するexeのパスと、失敗時のダイアログのタイトル)。
    /// </param>
    public UpdateMenuController(Func<string?, string, Task> restartApplication)
    {
        _restartApplication = restartApplication;

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
        versionText.SetResourceReference(TextBlock.ForegroundProperty, SystemColors.MenuTextBrushKey);
        versionText.Inlines.Add(_versionRun);
        versionText.Inlines.Add(_updateBadgeRun);

        // バージョン文字列と、更新可能なときだけ添える括弧書きのバッジ("（更新可能）")を1つの
        // TextBlock(Run2つ)にまとめてHeaderに使う。更新が無い間はIsEnabled=falseにして無効化する
        // (ホバー・クリックの見た目を出さないため)。
        MenuItem = new MenuItem
        {
            Header = versionText,
            IsEnabled = false,
        };
        MenuItem.Click += OnMenuItemClick;
    }

    /// <summary>トレイメニューに配置するバージョン表示項目。</summary>
    public MenuItem MenuItem { get; }

    /// <summary>
    /// 更新確認を開始する。監視サービスの初期化とは無関係な処理のため、独立してバックグラウンドで
    /// 実行する。起動時に即座に1回実行し(dueTime: Zero)、以後UpdateCheckIntervalおきに再確認する。
    /// </summary>
    public void Start()
    {
        // 二重に呼ばれても、タイマーが2本になって確認が重複しないようにする。
        if (_updateCheckTimer is not null)
        {
            return;
        }

        var dispatcher = MenuItem.Dispatcher;
        _updateCheckTimer = new System.Threading.Timer(
            _ => dispatcher.BeginInvoke(new Action(async () => await CheckForUpdateAsync()), DispatcherPriority.Background),
            null,
            TimeSpan.Zero,
            UpdateCheckInterval);
    }

    public void Dispose() => _updateCheckTimer?.Dispose();

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

            ApplyMenuState(currentVersion, update);
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
    private void ApplyMenuState(Version currentVersion, UpdateCheckService.UpdateInfo? update)
    {
        _versionRun.Text = FormatVersion(currentVersion);

        if (update is null)
        {
            _updateBadgeRun.Text = "";
            MenuItem.IsEnabled = false;
            _availableUpdate = null;
        }
        else
        {
            _updateBadgeRun.Text = "（更新可能）";
            MenuItem.IsEnabled = true;
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
    private async void OnMenuItemClick(object sender, RoutedEventArgs e)
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
            var confirm = await TrayDialogs.ShowMessageBoxAsync(
                $"{FormatVersion(update.LatestVersion)}に更新します。{sizeText}をダウンロードして、自動的に再起動します。\n\nよろしいですか？",
                "更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                _isUpdating = false;
                return;
            }

            MenuItem.IsEnabled = false;
            _updateBadgeRun.Text = "（更新中…）";
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
                var openPage = await TrayDialogs.ShowMessageBoxAsync(
                    $"自動更新に失敗しました。リリースページを開きますか？\n\n{ex.Message}",
                    "更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (openPage == MessageBoxResult.Yes)
                {
                    OpenReleasePage(update.HtmlUrl);
                }

                _isUpdating = false;
                ApplyMenuState(GetCurrentVersion(), update);
                return;
            }

            DiagnosticLog.Write("[自己更新] 置き換え完了、再起動します");
            await _restartApplication(exePath, "更新");
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
}
