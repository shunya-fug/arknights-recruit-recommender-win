using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// The full output of a single capture -> OCR -> tag match -> combination analysis pass.
/// Kept together (rather than just raising the good combinations) so a caller such as the
/// debug menu action can inspect every intermediate step when things don't work as expected.
/// </summary>
public sealed record RecruitmentCheckResult(
    BitmapSource Frame,
    IReadOnlyList<DetectedTag> RawOcrWords,
    IReadOnlyList<string> MatchedTags,
    IReadOnlyList<CombinationResult> Combinations);
