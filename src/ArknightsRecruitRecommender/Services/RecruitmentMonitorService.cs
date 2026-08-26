using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Models;
using Windows.Globalization;

namespace ArknightsRecruitRecommender.Services;

public sealed class RecruitmentMonitorService : IDisposable
{
    private const string GameWindowTitleHint = "Arknights";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromMilliseconds(500);

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

    public event Action<IReadOnlyList<CombinationResult>>? GoodCombinationsFound;

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
        var hwnd = WindowCaptureService.FindWindowByTitle(GameWindowTitleHint);
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
            if (result is null || result.MatchedTags.Count == 0)
            {
                return;
            }

            // Avoid re-notifying for the same tag set every poll cycle.
            if (_lastVisibleTags is not null && _lastVisibleTags.SequenceEqual(result.MatchedTags))
            {
                return;
            }

            _lastVisibleTags = result.MatchedTags;

            var goodCombinations = result.Combinations.Where(r => r.IsRecommended).ToList();
            if (goodCombinations.Count > 0)
            {
                GoodCombinationsFound?.Invoke(goodCombinations);
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
