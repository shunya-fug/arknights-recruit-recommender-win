using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

public sealed class RecruitmentMonitorService : IDisposable
{
    private const string GameWindowTitleHint = "Arknights";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(2);

    private readonly WindowCaptureService _captureService = new();
    private readonly TagOcrService _ocrService = new();
    private readonly RecruitmentAnalyzer _analyzer = new();
    private readonly IReadOnlyList<OperatorInfo> _operators;
    private readonly IReadOnlyList<string> _knownTags;

    private readonly System.Threading.Timer _timer;
    private IReadOnlyList<string>? _lastVisibleTags;

    public event Action<IReadOnlyList<CombinationResult>>? GoodCombinationsFound;

    public RecruitmentMonitorService(OperatorDataProvider dataProvider)
    {
        _operators = dataProvider.Load();
        _knownTags = OperatorDataProvider.GetAllKnownTags(_operators);
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.Zero, PollInterval);
    }

    /// <summary>
    /// Runs one capture -> OCR -> tag match -> combination analysis pass and returns every
    /// intermediate result. Used both by the background poll timer and by the tray "手動チェック実行"
    /// menu action for on-demand, manual verification against a real running game.
    ///
    /// The game's window is (re)detected on every call: if found, the capture session is started
    /// (or, if already running for this window, reused - this is what makes frequent polling
    /// cheap); if not found, any previously running session is torn down, so GPU resources are
    /// released as soon as the game closes.
    /// </summary>
    /// <returns>Null if the game window could not be found, or no frame could be captured.</returns>
    public async Task<RecruitmentCheckResult?> CheckOnceAsync()
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
    /// A freshly (re)started capture session has not produced a frame yet, so poll briefly for
    /// the first one rather than reporting failure immediately.
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
        try
        {
            var result = await CheckOnceAsync();
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
    }

    public void Dispose()
    {
        _timer.Dispose();
        _captureService.Dispose();
    }
}
