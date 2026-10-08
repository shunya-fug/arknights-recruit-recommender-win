using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public class RecruitmentAnalyzerTests
{
    private static readonly IReadOnlyList<OperatorInfo> Operators = new List<OperatorInfo>
    {
        new() { Name = "Robo", Rarity = 1, Tags = new[] { "Robot" } },
        new() { Name = "Starter", Rarity = 3, Tags = new[] { "Guard", "Melee", "Starter" } },
        new() { Name = "FourStarDps", Rarity = 4, Tags = new[] { "Guard", "Melee", "DPS" } },
        new() { Name = "SixStarDps", Rarity = 6, Tags = new[] { "Guard", "Melee", "DPS", "Top Operator" } },
        // "Top Operator"は全★6共通(=自動検出されるべきタグ)。"Summon"はこのオペレーター
        // だけが持つ、Kal'tsit(医療タイプ+召喚→★6)を模した特殊タグ。
        new() { Name = "SixStarSummoner", Rarity = 6, Tags = new[] { "Medic", "Summon", "Top Operator" } },
        new() { Name = "FiveStarCaster", Rarity = 5, Tags = new[] { "Caster", "AoE", "Senior Operator" } },
    };

    [Fact]
    public void SingleTagMatchingOnlyHighRarityOperators_IsRecommended()
    {
        var analyzer = new RecruitmentAnalyzer();

        var results = analyzer.Evaluate(new[] { "Senior Operator" }, Operators);

        var single = Assert.Single(results);
        Assert.Equal(5, single.GuaranteedMinRarity);
        Assert.True(single.IsRecommended);
    }

    [Fact]
    public void TagComboIncludingLowRarityOperator_IsNotRecommended()
    {
        var analyzer = new RecruitmentAnalyzer();

        var results = analyzer.Evaluate(new[] { "Guard", "Melee" }, Operators);

        var combo = results.Single(r => r.Tags.SequenceEqual(new[] { "Guard", "Melee" }));
        Assert.Equal(3, combo.GuaranteedMinRarity); // Starter (3*) drags the floor down
        Assert.False(combo.IsRecommended);
    }

    [Fact]
    public void ThreeTagComboNarrowingToTopOperatorOnly_IsRecommended()
    {
        var analyzer = new RecruitmentAnalyzer();

        var results = analyzer.Evaluate(new[] { "Guard", "Melee", "Top Operator" }, Operators);

        var combo = results.Single(r => r.Tags.Count == 3);
        Assert.Equal(6, combo.GuaranteedMinRarity);
        Assert.True(combo.IsRecommended);
    }

    [Fact]
    public void FourOrMoreTagSubsets_AreNeverEvaluated()
    {
        var analyzer = new RecruitmentAnalyzer();

        var results = analyzer.Evaluate(new[] { "Guard", "Melee", "DPS", "Top Operator" }, Operators);

        Assert.All(results, r => Assert.True(r.Tags.Count <= 3));
    }

    [Fact]
    public void TagWithNoMatchingOperators_IsExcludedFromResults()
    {
        var analyzer = new RecruitmentAnalyzer();

        var results = analyzer.Evaluate(new[] { "Nonexistent Tag" }, Operators);

        Assert.Empty(results);
    }

    /// <summary>
    /// ゲーム仕様上、★6は募集タグに"Top Operator"("上級エリート"相当)そのものを含めない限り
    /// 絶対に出現しない。単純な絞り込みロジックだけだと、他のタグの組み合わせがたまたま
    /// ★6オペレーター1人だけに一致した場合に誤って「確定」と報告してしまう
    /// (実例: 日本語版の「医療タイプ」+「召喚」の組み合わせがケルシー(★6)1人だけに一致するが、
    /// 「上級エリート」タグを含まないため実際には★6は出現しない)。
    /// </summary>
    [Fact]
    public void TagSubsetNarrowingToTopOperatorWithoutTopOperatorTag_IsExcludedFromResults()
    {
        var analyzer = new RecruitmentAnalyzer();

        // "Summon"はフィクスチャ上SixStarSummonerだけが持つタグ("Top Operator"は含まない)。
        var results = analyzer.Evaluate(new[] { "Summon" }, Operators);

        Assert.Empty(results);
    }

    /// <summary>
    /// ★6専用タグの自動検出は、候補となるタグ(全★6が持ち★6以外は誰も持たないタグ)が
    /// 2つ以上見つかった場合、どちらを採用すべきか判別できない。誤ったタグを勝手に
    /// 採用してしまう(=別の組み合わせを誤って★6特例の対象外にしてしまう)よりは、
    /// ★6特例自体を安全側に倒して適用しない(=既存の単純な絞り込みロジックにフォールバック
    /// する)べき、という意図を固定化するテスト。
    /// </summary>
    [Fact]
    public void AmbiguousTopOperatorTagCandidates_FallsBackToSimpleMatching()
    {
        var ambiguousOperators = new List<OperatorInfo>
        {
            new() { Name = "Starter", Rarity = 3, Tags = new[] { "Guard" } },
            // "Top Operator"と"Unique"のどちらも「全★6が持ち★6以外は誰も持たない」を
            // 満たしてしまい、自動検出が一意に決められないケース。
            new() { Name = "OnlySixStar", Rarity = 6, Tags = new[] { "Guard", "Top Operator", "Unique" } },
        };
        var analyzer = new RecruitmentAnalyzer();

        var results = analyzer.Evaluate(new[] { "Unique" }, ambiguousOperators);

        // 特例が無効化され、単純な絞り込みロジックのまま★6が返る(=フォールバック)。
        var single = Assert.Single(results);
        Assert.Equal(6, single.GuaranteedMinRarity);
    }

    [Fact]
    public void Band740_ExcludesLowRarityOperators_MakingTheComboRecommended()
    {
        // 爆発力(★1のTHRM-EXと★4以上が混在)を模したケース。区間指定なしでは★1が含まれるため
        // 確定にならないが、7:40〜9:00では★1が出ないため候補から外れ、★4以上確定になる。
        var operators = new List<OperatorInfo>
        {
            new() { Name = "Robo", Rarity = 1, Tags = new[] { "Burst" } },
            new() { Name = "Cutter", Rarity = 4, Tags = new[] { "Burst" } },
            new() { Name = "Fire", Rarity = 5, Tags = new[] { "Burst" } },
        };
        var analyzer = new RecruitmentAnalyzer();

        var withoutBand = analyzer.Evaluate(new[] { "Burst" }, operators).Single();
        var withBand = analyzer.Evaluate(new[] { "Burst" }, operators, RecruitTimeBand.From740).Single();

        Assert.False(withoutBand.IsRecommended);
        Assert.Equal(4, withBand.GuaranteedMinRarity);
        Assert.True(withBand.IsRecommended);
        Assert.DoesNotContain(withBand.MatchingOperators, o => o.Name == "Robo");
    }

    [Fact]
    public void ShortBand_ExcludesFiveStarAndAbove_AndLongBandKeepsSixStarOnlyWithTopTag()
    {
        var analyzer = new RecruitmentAnalyzer();

        // ★1〜4の区間では★5(FiveStarCaster)・★6が候補外なので、"Senior Operator"は結果が空。
        var shortBand = analyzer.Evaluate(new[] { "Senior Operator" }, Operators, RecruitTimeBand.UpTo350);
        Assert.Empty(shortBand);

        // ★6は7:40〜9:00のときだけ候補になり、さらに上級エリートタグが必要(従来仕様)。
        var midBand = analyzer.Evaluate(new[] { "Top Operator" }, Operators, RecruitTimeBand.From400To730);
        Assert.Empty(midBand);
        var longBand = analyzer.Evaluate(new[] { "Top Operator" }, Operators, RecruitTimeBand.From740);
        Assert.Equal(6, Assert.Single(longBand).GuaranteedMinRarity);
    }

    [Fact]
    public void RarityGainInLongerBand_ReportsHigherRarityComboHiddenByShortBand()
    {
        var analyzer = new RecruitmentAnalyzer();

        // ★1〜4の区間では★5(FiveStarCaster)が出ないため"Senior Operator"のおすすめが消える。
        // ★5が出始める4:00〜7:30の区間にすれば★5確定になるので、7:40以上ではなくその区間を案内する。
        var hint = analyzer.FindRarityGainInLongerBand(new[] { "Senior Operator" }, Operators, RecruitTimeBand.UpTo350);

        Assert.Equal(new BandRarityGain(RecruitTimeBand.From400To730, 5), hint);
    }

    [Fact]
    public void RarityGainInLongerBand_DoesNotWarnForCombosAlreadyShownAsTheTargetRarity()
    {
        // 爆発力(★1のTHRM-EXと★4以上が混在)を模したケース。★１ー４では「★1募集可能」のカードとして
        // 既に表示されている(★1を狙ってこの区間を選んでいる)ので、長い区間なら確定になることを
        // 毎回警告すると不要な警告になる。
        var operators = new List<OperatorInfo>
        {
            new() { Name = "Robo", Rarity = 1, Tags = new[] { "Burst" } },
            new() { Name = "Cutter", Rarity = 4, Tags = new[] { "Burst" } },
            new() { Name = "Fire", Rarity = 5, Tags = new[] { "Burst" } },
        };
        var analyzer = new RecruitmentAnalyzer();

        Assert.Null(analyzer.FindRarityGainInLongerBand(new[] { "Burst" }, operators, RecruitTimeBand.UpTo350));
    }

    [Fact]
    public void RarityGainInLongerBand_PointsToTheShortestBandWhereAnUndisplayedComboBecomesRecommended()
    {
        // ★2と★4が混在する組み合わせ。★１ー４(狙うのは★1)では、★1を含まず確定でもないので表示されない。
        // ★2が出なくなる7:40以降で★4確定になるので、4:00〜7:30(★2が残る)ではなく7:40以上を案内する。
        var operators = new List<OperatorInfo>
        {
            new() { Name = "Two", Rarity = 2, Tags = new[] { "Mix" } },
            new() { Name = "Four", Rarity = 4, Tags = new[] { "Mix" } },
        };
        var analyzer = new RecruitmentAnalyzer();

        Assert.Equal(
            new BandRarityGain(RecruitTimeBand.From740, 4),
            analyzer.FindRarityGainInLongerBand(new[] { "Mix" }, operators, RecruitTimeBand.UpTo350));
    }

    [Fact]
    public void RarityGainInLongerBand_IsNullInTheLongestBandOrWhenNothingGained()
    {
        var analyzer = new RecruitmentAnalyzer();

        Assert.Null(analyzer.FindRarityGainInLongerBand(new[] { "Senior Operator" }, Operators, RecruitTimeBand.From740));
        // "Guard"系は区間を変えてもおすすめの有無・確定レアリティが変わらない。
        Assert.Null(analyzer.FindRarityGainInLongerBand(new[] { "Guard" }, Operators, RecruitTimeBand.From400To730));
    }

    private static readonly IReadOnlyList<OperatorInfo> LowRarityOperators = new List<OperatorInfo>
    {
        new() { Name = "Robo1", Rarity = 1, Tags = new[] { "X" } },
        new() { Name = "Two", Rarity = 2, Tags = new[] { "X", "Z" } },
        new() { Name = "Four1", Rarity = 4, Tags = new[] { "X", "Y" } },
        new() { Name = "Robo2", Rarity = 1, Tags = new[] { "Y" } },
        new() { Name = "Four2", Rarity = 4, Tags = new[] { "Y" } },
        new() { Name = "Four3", Rarity = 4, Tags = new[] { "Y", "W" } },
    };

    [Fact]
    public void SelectForDisplay_InTheNormalBand_ReturnsOnlyRecommendedCombos()
    {
        var analyzer = new RecruitmentAnalyzer();
        var all = analyzer.Evaluate(new[] { "Senior Operator", "Guard" }, Operators, RecruitTimeBand.From740);

        var displayed = RecruitmentAnalyzer.SelectForDisplay(all, RecruitTimeBand.From740);

        Assert.Equal(all.Where(c => c.IsRecommended), displayed);
    }

    [Fact]
    public void SelectForDisplay_InTheShortestBand_ListsConfirmedCombosFirst_ThenOneStarCombosByShare()
    {
        var analyzer = new RecruitmentAnalyzer();
        var all = analyzer.Evaluate(new[] { "X", "Y", "W" }, LowRarityOperators, RecruitTimeBand.UpTo350);

        var displayed = RecruitmentAnalyzer.SelectForDisplay(all, RecruitTimeBand.UpTo350);

        // ★4以上確定([W]・[X,Y]など、★1を含まず全員★4のプール)が先頭。続けて、★1を含む組み合わせ。
        var confirmedCount = all.Count(c => c.IsRecommended);
        Assert.True(confirmedCount > 0);
        Assert.All(displayed.Take(confirmedCount), c => Assert.True(c.IsRecommended));

        var targeted = displayed.Skip(confirmedCount).ToList();
        Assert.All(targeted, c => Assert.Contains(c.MatchingOperators, o => o.Rarity == 1));
        // 通知の表示は、狙うレアリティを下限(GuaranteedMinRarity)から求めるので、一致していること。
        Assert.All(targeted, c => Assert.Equal(1, c.GuaranteedMinRarity));
        // [X]: Robo1/Two/Four1 → ★1は1/3。[Y]: Robo2/Four1/Four2/Four3 → 1/4。当たりやすい順。
        Assert.Equal(
            new[] { "X", "Y" },
            targeted.Where(c => c.Tags.Count == 1).Select(c => string.Join(",", c.Tags)));

        var shares = targeted.Select(c => (double)c.MatchingOperators.Count(o => o.Rarity == 1) / c.MatchingOperators.Count).ToList();
        Assert.Equal(shares.OrderByDescending(s => s), shares);
    }

    [Fact]
    public void SelectForDisplay_InTheMidBand_TargetsTwoStarsAndIgnoresOneStars()
    {
        var analyzer = new RecruitmentAnalyzer();
        var all = analyzer.Evaluate(new[] { "X", "Z" }, LowRarityOperators, RecruitTimeBand.From400To730);

        var displayed = RecruitmentAnalyzer.SelectForDisplay(all, RecruitTimeBand.From400To730);

        // ★1(Robo1)は4:00以降は出ないので候補外。★2(Two)を含む[X]・[Z]・[X,Z]が出る。
        Assert.NotEmpty(displayed);
        Assert.All(displayed, c => Assert.DoesNotContain(c.MatchingOperators, o => o.Rarity == 1));
        Assert.All(displayed.Where(c => !c.IsRecommended), c => Assert.Contains(c.MatchingOperators, o => o.Rarity == 2));
        Assert.All(displayed.Where(c => !c.IsRecommended), c => Assert.Equal(2, c.GuaranteedMinRarity));
    }

    [Fact]
    public void FullTimeWarning_AppliesOnlyToFiveStarAndAboveConfirmedCombos()
    {
        var analyzer = new RecruitmentAnalyzer();

        List<CombinationResult> Displayed(params string[] tags) =>
            RecruitmentAnalyzer.SelectForDisplay(
                analyzer.Evaluate(tags, Operators, RecruitTimeBand.From740), RecruitTimeBand.From740).ToList();

        // ★4確定(FourStarDps)は対象外。
        Assert.False(RecruitmentAnalyzer.RequiresFullTimeRecruitment(Displayed("DPS")));
        // ★5確定(エリート相当)。
        Assert.True(RecruitmentAnalyzer.RequiresFullTimeRecruitment(Displayed("Senior Operator")));
        // ★6確定のみ(上級エリート相当)。
        Assert.True(RecruitmentAnalyzer.RequiresFullTimeRecruitment(Displayed("Top Operator")));
        // ★5と★6が両方ある場合も対象。
        Assert.True(RecruitmentAnalyzer.RequiresFullTimeRecruitment(Displayed("Senior Operator", "Top Operator")));
        // 何も表示されていないときは警告なし。
        Assert.False(RecruitmentAnalyzer.RequiresFullTimeRecruitment(new List<CombinationResult>()));
    }
}
