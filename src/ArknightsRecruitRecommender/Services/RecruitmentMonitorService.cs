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

    private async Task TickAsync()
    {
        try
        {
            var hwnd = WindowCaptureService.FindWindowByTitle(GameWindowTitleHint);
            if (hwnd is null)
            {
                return;
            }

            var frame = await _captureService.CaptureFrameAsync(hwnd.Value);
            var detected = await _ocrService.RecognizeAsync(frame);
            var visibleTags = TagMatcher.MatchKnownTags(detected, _knownTags);

            if (visibleTags.Count == 0)
            {
                return;
            }

            // Avoid re-notifying for the same tag set every poll cycle.
            if (_lastVisibleTags is not null && _lastVisibleTags.SequenceEqual(visibleTags))
            {
                return;
            }

            _lastVisibleTags = visibleTags;

            var results = _analyzer.Evaluate(visibleTags, _operators);
            var goodCombinations = results.Where(r => r.IsRecommended).ToList();

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
