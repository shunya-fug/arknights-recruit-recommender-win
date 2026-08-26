using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

public sealed class RecruitmentMonitorService : IDisposable
{
    private const string GameWindowTitleHint = "Arknights";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

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
    /// </summary>
    /// <returns>Null if the game window could not be found.</returns>
    public async Task<RecruitmentCheckResult?> CheckOnceAsync()
    {
        var hwnd = WindowCaptureService.FindWindowByTitle(GameWindowTitleHint);
        if (hwnd is null)
        {
            return null;
        }

        var frame = await _captureService.CaptureFrameAsync(hwnd.Value);
        var detected = await _ocrService.RecognizeAsync(frame);
        var visibleTags = TagMatcher.MatchKnownTags(detected, _knownTags);
        var combinations = _analyzer.Evaluate(visibleTags, _operators);

        return new RecruitmentCheckResult(frame, detected, visibleTags, combinations);
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
