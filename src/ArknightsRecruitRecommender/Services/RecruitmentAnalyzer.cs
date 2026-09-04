using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// Evaluates the recruitment tag guarantee logic: any subset of 1-3 selected tags whose
/// matching operator pool has a minimum rarity of 4 stars or higher is a "good" combination.
/// This mirrors the game's own rule (subsets larger than 3 tags are never evaluated), so no
/// special-casing for robots / senior operators is needed as long as the operator data's tag
/// lists are accurate.
///
/// 唯一の例外が★6("上級エリート"タグを持つオペレーター): ゲーム仕様上、★6は募集タグに
/// "上級エリート"(EN版では"Top Operator")そのものを含めない限り絶対に出現しない
/// (実機確認・攻略サイト複数で確認済み: https://kamigame.jp/arknights/page/344861011829888618.html)。
/// タグの絞り込みの結果たまたま★6オペレーター1人だけに一致しても、それだけでは"確定"にならない
/// ため、このタグを含まない組み合わせでは★6を候補から除外する。★3〜★5はこの特例が無く、
/// 単純な絞り込みロジックのままで正しい(募集時間との兼ね合いは画面側で確認可能なため、
/// アプリ側では扱わない)。
/// </summary>
public sealed class RecruitmentAnalyzer
{
    public IReadOnlyList<CombinationResult> Evaluate(IReadOnlyList<string> visibleTags, IReadOnlyList<OperatorInfo> operators)
    {
        var results = new List<CombinationResult>();
        var topOperatorTag = FindTopOperatorTag(operators);

        foreach (var subset in GetSubsets(visibleTags, maxSize: 3))
        {
            var matches = operators
                .Where(op => subset.All(tag => op.Tags.Contains(tag)))
                .ToList();

            if (topOperatorTag is not null && !subset.Contains(topOperatorTag))
            {
                matches = matches.Where(op => op.Rarity != 6).ToList();
            }

            if (matches.Count == 0)
            {
                continue;
            }

            results.Add(new CombinationResult
            {
                Tags = subset,
                GuaranteedMinRarity = matches.Min(op => op.Rarity),
                MatchingOperators = matches,
            });
        }

        return results
            .OrderByDescending(r => r.GuaranteedMinRarity)
            .ThenByDescending(r => r.Tags.Count)
            .ToList();
    }

    /// <summary>
    /// "上級エリート"相当のタグを、文字列決め打ちせずデータから動的に特定する
    /// (Data/operators.{locale}.jsonはロケールごとに表記が異なるため。例:
    /// en-US版では"Top Operator")。「全★6オペレーターが持ち、★6以外のオペレーターは
    /// 誰一人持たない」という性質で一意に特定できる(operators.ja-JP.json / en-US.json
    /// 双方で成立することを確認済み)。
    ///
    /// 候補タグは★6オペレーター全員のタグの和集合から探す(特定の1人だけを見ると、
    /// その1人のタグ付けが万一欠落していた場合に検出漏れになるため)。該当タグが0件、
    /// または(データ不備等で)複数該当してどれを採用すべきか判別できない場合はログに
    /// 記録した上でnullを返し、★6特例を適用しない(既存の単純な絞り込みロジックに
    /// フォールバックする。誤ったタグを勝手に採用するより安全なため)。
    /// </summary>
    private static string? FindTopOperatorTag(IReadOnlyList<OperatorInfo> operators)
    {
        var topOperators = operators.Where(op => op.Rarity == 6).ToList();
        if (topOperators.Count == 0)
        {
            return null;
        }

        var nonTopOperators = operators.Where(op => op.Rarity != 6).ToList();
        var candidates = topOperators
            .SelectMany(op => op.Tags)
            .Distinct()
            .Where(tag =>
                topOperators.All(op => op.Tags.Contains(tag)) &&
                nonTopOperators.All(op => !op.Tags.Contains(tag)))
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        DiagnosticLog.Write(
            $"[RecruitmentAnalyzer] ★6専用タグの自動特定に失敗しました(候補{candidates.Count}件: " +
            $"{string.Join(" / ", candidates)})。★6の誤確定防止の特例を適用せず続行します。");
        return null;
    }

    private static IEnumerable<IReadOnlyList<string>> GetSubsets(IReadOnlyList<string> tags, int maxSize)
    {
        var count = tags.Count;
        for (var mask = 1; mask < (1 << count); mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) > maxSize)
            {
                continue;
            }

            var subset = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    subset.Add(tags[i]);
                }
            }

            yield return subset;
        }
    }
}
