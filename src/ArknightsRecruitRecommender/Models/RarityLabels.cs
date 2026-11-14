namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// レアリティの表記(「★４」「★４以上確定」など)。通知ウィンドウ・手動チェックのデバッグ出力・
/// 開発補助ツールで同じ表記になるよう、ここにまとめている。数字はゲーム内の表記に合わせて全角にする。
/// </summary>
public static class RarityLabels
{
    /// <summary>「★４」の表記。</summary>
    public static string Star(int rarity) => $"★{ToFullWidthDigits(rarity)}";

    /// <summary>「★４以上確定」の表記。★６は上限なので「以上」を付けず「★６確定」とする。</summary>
    public static string Guarantee(int rarity) =>
        rarity >= 6 ? $"{Star(rarity)}確定" : $"{Star(rarity)}以上確定";

    private static string ToFullWidthDigits(int value) =>
        new(value.ToString().Select(c => (char)('０' + (c - '0'))).ToArray());
}
