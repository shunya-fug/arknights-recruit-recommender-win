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
/// 単純な絞り込みロジックのままで正しい。募集時間による排出レアリティの制限は、呼び出し側が
/// 区間(<see cref="RecruitTimeBand"/>)を指定した場合のみ反映する。
/// </summary>
public sealed class RecruitmentAnalyzer
{
    /// <param name="band">
    /// 想定する募集時間の区間(Issue #34)。指定すると、その区間で排出されないレアリティの
    /// オペレーターを候補から外して判定する(例: 7:40〜9:00なら★1・★2は出ないため、それらを
    /// 含むために確定にならなかった組み合わせが★4以上確定になりうる)。nullなら全レアリティを
    /// 対象にする。
    /// </param>
    public IReadOnlyList<CombinationResult> Evaluate(
        IReadOnlyList<string> visibleTags,
        IReadOnlyList<OperatorInfo> operators,
        RecruitTimeBand? band = null)
    {
        var results = new List<CombinationResult>();
        // ★6専用タグは、募集時間の区間で絞り込む前の全オペレーターから特定する
        // (区間によって★6が候補から外れても、タグ自体の特定結果は変わらないため)。
        var topOperatorTag = FindTopOperatorTag(operators);

        foreach (var subset in GetSubsets(visibleTags, maxSize: 3))
        {
            var matches = operators
                .Where(op => band is null || band.Value.AllowsRarity(op.Rarity))
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
    /// 選択中の募集時間の区間では表示されていない組み合わせが、より長い区間なら★4以上確定になる
    /// 場合に、最初にそうなる(最短の)区間と、その区間で増える分の最高の確定レアリティを返す。
    /// 無ければnull(Issue #34)。
    ///
    /// 「★１ー４」「★２ー５」などの区間では、範囲外のレアリティが候補から外れるため、その
    /// 組み合わせが通知されない(★5が出始めるのは4:00以上、★1・★2が出なくなるのは7:40以上)。
    /// ゲーム内では募集時間は見てから変えられるので、「通知が無い」ではなく「別の区間にすれば
    /// 出る」ことに気づけるようにするためのヒント。最短の区間を案内するのは、実際には4:00〜7:30
    /// でも確定になる組み合わせを、7:40以上と案内して不正確にしないため。さらに長い区間でしか
    /// 増えない組み合わせは、案内された区間に切り替えた後の判定で次のヒントとして出る。
    ///
    /// 既にカードとして表示されている組み合わせは、見逃しではないため対象にしない。特に、短い区間で
    /// 狙うレアリティ(★1・★2)が出る組み合わせは、そのレアリティを狙ってその区間を選んでいるのに、
    /// 長い区間ならその分が外れて確定になるのが通常で、毎回案内すると不要な警告になる。
    /// </summary>
    public BandRarityGain? FindRarityGainInLongerBand(
        IReadOnlyList<string> visibleTags,
        IReadOnlyList<OperatorInfo> operators,
        RecruitTimeBand band)
    {
        // 選択中の区間で表示される組み合わせ。確定のものは確定レアリティ、狙うレアリティの
        // 組み合わせ(確定ではないが表示されている)は、どの長い区間でも「増えた」とみなさないよう最大値にする。
        var displayed = SelectForDisplay(Evaluate(visibleTags, operators, band), band)
            .ToDictionary(r => CombinationKey(r), r => r.IsRecommended ? r.GuaranteedMinRarity ?? 0 : int.MaxValue);

        // 区間の並び(RecruitTimeBands.All)は短い順なので、選択中の区間より後ろが「長い区間」。
        foreach (var longer in RecruitTimeBands.All.SkipWhile(b => b != band).Skip(1))
        {
            var gains = Evaluate(visibleTags, operators, longer)
                .Where(r => r.IsRecommended)
                .Where(r => !displayed.TryGetValue(CombinationKey(r), out var rarity) || (r.GuaranteedMinRarity ?? 0) > rarity)
                .Select(r => r.GuaranteedMinRarity ?? 0)
                .ToList();

            if (gains.Count > 0)
            {
                return new BandRarityGain(longer, gains.Max());
            }
        }

        return null;
    }

    /// <summary>
    /// これ以上の確定レアリティ(エリート=★5、上級エリート=★6)を当てにするには、募集時間を9:00に
    /// する必要がある。9:00未満だと選んだタグが外れる(タグ消し)ことがあり、9:00なら外れない
    /// (攻略サイト・Terra Wikiで確認)。★4確定は件数が多く、毎回出ると目障りなので対象にしない。
    /// </summary>
    public const int MinRarityRequiringFullTime = 5;

    /// <summary>
    /// 表示中の組み合わせに、募集時間を9:00にしないとタグが外れることがある確定
    /// (<see cref="MinRarityRequiringFullTime"/>以上)が含まれるか。通知の「9:00推奨」警告を出す条件。
    /// </summary>
    public static bool RequiresFullTimeRecruitment(IReadOnlyList<CombinationResult> displayed) =>
        displayed.Any(c => c.IsRecommended && c.GuaranteedMinRarity >= MinRarityRequiringFullTime);

    /// <summary>
    /// 通知に表示する組み合わせを、想定する募集時間の区間に応じて選ぶ(Issue #34)。
    ///
    /// どの区間でも、★4以上が確定する組み合わせ(<see cref="CombinationResult.IsRecommended"/>)を
    /// 先頭に表示する(価値が高く件数も少ないため。短い区間でこれを隠すと、7:40〜9:00と結果が
    /// 同じで警告も出ないため、見落としに気づけない)。
    /// 短い区間(〜3:50・4:00〜7:30)は、これに続けて、その区間で狙うレアリティ
    /// (<see cref="RecruitTimeBands.TargetRarity"/>、★1・★2)のオペレーターが1人でも出うる組み合わせを、
    /// そのレアリティの割合が高い(当たりやすい)順に返す。ロボットなど通常の運用では出ない低レアを
    /// 狙うときに、この区間を選ぶため。
    ///
    /// <paramref name="combinations"/>は、同じ区間で<see cref="Evaluate"/>した結果であること
    /// (区間で出ないレアリティは、既に候補から外れている前提)。
    /// </summary>
    public static IReadOnlyList<CombinationResult> SelectForDisplay(
        IReadOnlyList<CombinationResult> combinations,
        RecruitTimeBand band)
    {
        var recommended = combinations.Where(c => c.IsRecommended).ToList();
        if (band.TargetRarity() is not { } target)
        {
            return recommended;
        }

        // ★4以上確定の組み合わせは最低★4なので、狙うレアリティ(★1・★2)を含むことは無く、
        // 上のrecommendedと重複しない。
        var targeted = combinations
            .Where(c => c.MatchingOperators.Any(o => o.Rarity == target))
            .OrderByDescending(c => (double)c.MatchingOperators.Count(o => o.Rarity == target) / c.MatchingOperators.Count)
            .ThenByDescending(c => c.Tags.Count);

        return recommended.Concat(targeted).ToList();
    }

    private static string CombinationKey(CombinationResult r) => string.Join('\u0001', r.Tags);

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
