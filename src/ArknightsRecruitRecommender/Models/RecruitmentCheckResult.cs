using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// キャプチャ→OCR→タグ照合→組み合わせ判定1回分の全出力。おすすめ組み合わせだけでなく途中経過も
/// まとめて保持することで、手動チェック実行のような呼び出し元が、期待通りに動かない時に
/// 各段階の結果を確認できるようにしている。
/// </summary>
public sealed record RecruitmentCheckResult(
    BitmapSource Frame,
    IReadOnlyList<DetectedTag> RawOcrWords,
    IReadOnlyList<string> MatchedTags,
    IReadOnlyList<CombinationResult> Combinations);
