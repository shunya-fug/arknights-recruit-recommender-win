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
}
