namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// 日本語のようにスペースで単語が区切られない言語では、Windows OCRが1つのUIラベル
/// (例:「狙撃タイプ」)を1文字ずつバラバラの単語として返すことがある。実機検証で確認済み。
/// 個々の文字はタグ名(複数文字)とは一致しないため、既知タグとの照合の前に、同じ行かつ
/// 近接した単語同士を結合してラベル単位の文字列に復元する。
/// </summary>
public static class OcrWordClusterer
{
    public static IReadOnlyList<DetectedTag> Cluster(IReadOnlyList<DetectedTag> words)
    {
        var runs = new List<DetectedTag>();
        foreach (var row in GroupIntoRows(words))
        {
            runs.AddRange(MergeHorizontally(row.OrderBy(w => w.X).ToList()));
        }

        return runs;
    }

    /// <summary>
    /// まずY座標だけを見て行にまとめる。同じ行内の文字でもOCRのバウンディングボックスは
    /// 実機で数px程度ブレる(例:「プ」だけY=788、他はY=791)ため、Y→Xの複合ソートで
    /// 一度に並べ替えるとこのブレだけで行の途中に割り込んでしまい、読み順が崩れる。
    /// 各行の基準Yはその行で最初に見つかった単語のYに固定する(直前の単語との差分を
    /// 順番に見ていく実装だと、緩やかなドリフトが連鎖して離れた行まで結合してしまうため)。
    /// </summary>
    private static List<List<DetectedTag>> GroupIntoRows(IReadOnlyList<DetectedTag> words)
    {
        var sortedByY = words.OrderBy(w => w.Y).ToList();
        var rows = new List<List<DetectedTag>>();
        List<DetectedTag>? currentRow = null;
        double rowAnchorY = 0;

        foreach (var word in sortedByY)
        {
            if (currentRow is not null && Math.Abs(word.Y - rowAnchorY) <= word.Height * 0.5)
            {
                currentRow.Add(word);
            }
            else
            {
                currentRow = new List<DetectedTag> { word };
                rows.Add(currentRow);
                rowAnchorY = word.Y;
            }
        }

        return rows;
    }

    /// <summary>
    /// 実機のOCR結果(狙撃タイプ等)を計測した値: 同じラベル内の文字同士の間隔は文字の高さより
    /// 大幅に小さく(実測3〜9px、高さ約34px)、別々のボタン間の間隔ははるかに大きい
    /// (実測115px)。閾値を文字の高さと同程度に取ることで、解像度が変わっても両者を
    /// 明確に区別できる。
    /// </summary>
    private static List<DetectedTag> MergeHorizontally(List<DetectedTag> orderedByX)
    {
        var runs = new List<DetectedTag>();
        DetectedTag? current = null;

        foreach (var word in orderedByX)
        {
            if (current is not null && IsHorizontallyAdjacent(current, word))
            {
                current = Merge(current, word);
            }
            else
            {
                if (current is not null)
                {
                    runs.Add(current);
                }

                current = word;
            }
        }

        if (current is not null)
        {
            runs.Add(current);
        }

        return runs;
    }

    private static bool IsHorizontallyAdjacent(DetectedTag current, DetectedTag next)
    {
        var averageHeight = (current.Height + next.Height) / 2;
        var gap = next.X - (current.X + current.Width);
        return gap <= averageHeight;
    }

    private static DetectedTag Merge(DetectedTag current, DetectedTag next)
    {
        var minX = Math.Min(current.X, next.X);
        var minY = Math.Min(current.Y, next.Y);
        var maxX = Math.Max(current.X + current.Width, next.X + next.Width);
        var maxY = Math.Max(current.Y + current.Height, next.Y + next.Height);
        return new DetectedTag(current.Text + next.Text, minX, minY, maxX - minX, maxY - minY);
    }
}
