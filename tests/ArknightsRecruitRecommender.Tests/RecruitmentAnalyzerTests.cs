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
}
