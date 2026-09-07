using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Models;
using Windows.Globalization;

namespace ArknightsRecruitRecommender.Services;

public sealed class RecruitmentMonitorService : IDisposable
{
    // 実機確認済み: PC版アークナイツの実行ファイル名(表示言語に関わらず共通)。
    private const string GameProcessName = "Arknights";
    // 実機計測(Issue #6調査): capture+OCR+判定の合計処理時間は通常190〜230ms程度(稀に
    // 250ms超)。以前は200msだったが、これだと1ティックの処理がポーリング間隔を超えてしまい、
    // 次のティックが(_checkGateが空くまで)スキップされ続け、実質的なポーリング間隔が
    // 意図した200msの倍(約400ms)まで悪化していた。実測の処理時間に対して余裕を持たせるため
    // 300msに緩めた(OCR自体を縮小して高速化する案もあったが、精度とのトレードオフになる
    // ため見送り、素直に間隔を実測値に合わせる方を選んだ)。
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromMilliseconds(100);

    // 公開求人のタグ選択画面かどうかを判定するための閾値。実機のスクリーンショットで確認した
    // ところ、この画面では既知タグ全種類(現行データで29種類)ではなく、1枠あたり5〜6個の
    // タグボタンがランダムに表示される仕様だった(全タグから3個まで選んで組み合わせる)。
    // 一方、それ以外の画面(敵図鑑・オペレーター詳細等)でも、タグ名と同じ単語が説明文の一部として
    // 使われているケースがあり、偶然ちょうど3個一致して誤通知を引き起こすことを実機で確認した
    // (例:「元素耐性」「防御力を400無視」「遠距離術」からそれぞれ元素/防御/遠距離が一致し、
    // 3タグ揃って「おすすめ」判定になった)。実際の公開求人画面ではOCRの読み落としが1個程度
    // 発生しても4〜5個は検出できている実績があるため、4に引き上げて誤検出との差を広げた。
    private const int MinMatchedTagsForRecruitmentScreen = 4;

    private readonly WindowCaptureService _captureService = new();
    private readonly TagOcrService _ocrService;
    private readonly RecruitmentAnalyzer _analyzer = new();
    private readonly IReadOnlyList<OperatorInfo> _operators;
    private readonly IReadOnlyList<string> _knownTags;

    // _captureServiceは内部で可変状態(セッション)を持ちスレッドセーフではないため、常時監視の
    // タイマーコールバックと手動チェック実行の両方から同時に呼ばれないようこのゲートで直列化する。
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    private readonly System.Threading.Timer _timer;
    private IReadOnlyList<string>? _lastVisibleTags;
    private bool _isOnRecruitmentScreen;

    // OCRは1ティック目だけタグを1個見落とし、次のティックで揃うことがある(実績あり)。
    // 「おすすめ無し」を即座に確定させると、その1ティック後に本来のおすすめが現れて
    // 表示が反転してしまう。「おすすめ有り」は即座に表示する一方(応答性を優先)、
    // 「おすすめ無し」だけは同じタグの組み合わせが連続2ティック観測されるまで確定を待つ
    // (最大でもPollInterval1回分の遅延で済む)。
    private IReadOnlyList<string>? _pendingNoRecommendationTags;

    /// <summary>
    /// 公開求人画面で検出タグの組み合わせが変わるたびに発火する(おすすめの組み合わせが
    /// 無い場合も含む)。「おすすめ無し」を判定中(まだOCR結果が確定していない)と区別できる
    /// よう、購読側は空リストの場合も「該当なし」として明示的に表示すること。
    /// </summary>
    public event Action<IReadOnlyList<CombinationResult>>? RecommendationsUpdated;

    /// <summary>
    /// 公開求人のタグ選択画面を検出できなくなった(＝タグ一致数が閾値未満になった、または
    /// ゲームウィンドウ自体が見つからなくなった)ことを通知する。通知ウィンドウを、時間経過
    /// ではなく画面から離れたタイミングで閉じるために使う。
    /// </summary>
    public event Action? RecruitmentScreenLost;

    /// <param name="locale">
    /// 使用するオペレーターデータとOCR言語を一致させるためのロケール(例:"ja-JP")。
    /// ゲームの表示言語とOSの言語設定は必ずしも一致しないため、OSのプロファイル言語からの
    /// 自動選択には頼らず、常にこの値を明示的にOCRエンジンへ渡す。
    /// </param>
    public RecruitmentMonitorService(string locale)
    {
        _operators = new OperatorDataProvider(locale: locale).Load();
        _knownTags = OperatorDataProvider.GetAllKnownTags(_operators);
        _ocrService = new TagOcrService(new Language(locale));
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, PollInterval);
    }

    /// <summary>
    /// キャプチャ→OCR→タグ照合→組み合わせ判定を1回分実行し、途中経過も含めた結果を返す。
    /// 常時監視のポーリングタイマーと、トレイメニュー「手動チェック実行」による手動実行の
    /// 両方から呼ばれる。呼び出しは<see cref="_checkGate"/>で直列化しているため、両者が
    /// 同時に発生しても<see cref="_captureService"/>の内部状態が競合することはない。
    /// </summary>
    /// <returns>ゲームウィンドウが見つからない、またはフレームを取得できなかった場合はnull。</returns>
    public async Task<RecruitmentCheckResult?> CheckOnceAsync()
    {
        await _checkGate.WaitAsync();
        try
        {
            return await CheckOnceCoreAsync();
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// 実際のキャプチャ→OCR→判定処理本体。<see cref="_checkGate"/>を確保した状態でのみ
    /// 呼び出すこと（<see cref="_captureService"/>がスレッドセーフでないため）。
    ///
    /// ゲームウィンドウは呼び出しのたびに再検出する。見つかればキャプチャセッションを開始
    /// （同じウィンドウで起動済みならそのまま使い回す。これが高頻度ポーリングを安く保つ理由）、
    /// 見つからなければセッションを破棄し、ゲーム終了直後にGPUリソースを解放する。
    /// </summary>
    private async Task<RecruitmentCheckResult?> CheckOnceCoreAsync()
    {
        var hwnd = WindowCaptureService.FindWindowByProcessName(GameProcessName);
        if (hwnd is null)
        {
            _captureService.StopSession();
            return null;
        }

        _captureService.EnsureSessionStarted(hwnd.Value);

        var frame = await CaptureFirstAvailableFrameAsync();
        if (frame is null)
        {
            return null;
        }

        var detected = await _ocrService.RecognizeAsync(frame);
        var visibleTags = TagMatcher.MatchKnownTags(detected, _knownTags);
        var combinations = _analyzer.Evaluate(visibleTags, _operators);

        return new RecruitmentCheckResult(frame, detected, visibleTags, combinations);
    }

    /// <summary>
    /// セッションを開始した直後はまだ1枚もフレームが届いていないことがあるため、即座に失敗と
    /// せず短時間だけ再試行する。FirstFrameTimeoutはPollIntervalより十分短く保ち、1回の
    /// チェックがポーリング間隔をまたいで次のティックと重ならないようにしている。
    /// </summary>
    private async Task<BitmapSource?> CaptureFirstAvailableFrameAsync()
    {
        var deadline = DateTime.UtcNow + FirstFrameTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var frame = await _captureService.TryGetLatestFrameAsync();
            if (frame is not null)
            {
                return frame;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private async Task TickAsync()
    {
        // 前回分のチェックがまだ実行中なら、キューイングして積み上げるのではずスキップする
        // (どのみち直後にまた1秒後のティックが来るため、待ち行列を作る意味が薄い)。
        if (!await _checkGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var result = await CheckOnceCoreAsync();
            var isOnRecruitmentScreen = result is not null && result.MatchedTags.Count >= MinMatchedTagsForRecruitmentScreen;

            if (!isOnRecruitmentScreen)
            {
                // 画面から離れた瞬間(検出できていた状態→できなくなった状態への遷移)だけ通知する。
                // 見失ったままの状態が続く間、毎ティック無駄にイベントを発火させないため。
                if (_isOnRecruitmentScreen)
                {
                    RecruitmentScreenLost?.Invoke();
                }

                _isOnRecruitmentScreen = false;
                _lastVisibleTags = null;
                _pendingNoRecommendationTags = null;
                return;
            }

            _isOnRecruitmentScreen = true;

            // Avoid re-notifying for the same tag set every poll cycle.
            if (_lastVisibleTags is not null && _lastVisibleTags.SequenceEqual(result!.MatchedTags))
            {
                return;
            }

            // おすすめが0件の場合も含めて必ず発火する。0件を握りつぶすと、「まだ判定中で
            // 何も表示されていない」のか「判定済みでおすすめが無い」のかを画面から区別できず、
            // また直前に別のタグの組み合わせでおすすめが表示されていた場合、タグが変わって
            // おすすめが無くなった後もその古い通知が消えずに残ってしまう(実際に発生していた不具合)。
            var goodCombinations = result!.Combinations.Where(r => r.IsRecommended).ToList();

            if (goodCombinations.Count > 0)
            {
                _lastVisibleTags = result.MatchedTags;
                _pendingNoRecommendationTags = null;
                RecommendationsUpdated?.Invoke(goodCombinations);
                return;
            }

            // 「おすすめ無し」は即確定させず、同じタグの組み合わせがもう1ティック続いた
            // ことを確認してから確定する(上のフィールド宣言のコメント参照)。
            if (_pendingNoRecommendationTags is not null && _pendingNoRecommendationTags.SequenceEqual(result.MatchedTags))
            {
                _lastVisibleTags = result.MatchedTags;
                _pendingNoRecommendationTags = null;
                RecommendationsUpdated?.Invoke(goodCombinations);
            }
            else
            {
                _pendingNoRecommendationTags = result.MatchedTags;
            }
        }
        catch
        {
            // A single failed capture/OCR cycle (e.g. window minimized, game not on the
            // recruitment screen) should not crash the background monitor - just skip this tick.
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _captureService.Dispose();
        _checkGate.Dispose();
    }
}
