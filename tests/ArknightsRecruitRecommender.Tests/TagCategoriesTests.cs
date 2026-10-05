using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public class TagCategoriesTests
{
    [Fact]
    public void AllJaJpTags_AreCategorizedWithoutOthersGroup()
    {
        var operators = new OperatorDataProvider("ja-JP").Load();
        var knownTags = OperatorDataProvider.GetAllKnownTags(operators);

        var groups = TagCategories.Group(knownTags);

        Assert.DoesNotContain(groups, g => g.Name == "その他");
        Assert.Equal(
            knownTags.OrderBy(t => t),
            groups.SelectMany(g => g.Tags).OrderBy(t => t));
    }

    [Fact]
    public void UncategorizedTags_GoToTrailingOthersGroup()
    {
        var groups = TagCategories.Group(["近距離", "火力", "未知のタグ"]);

        Assert.Equal(["配置", "特性", "その他"], groups.Select(g => g.Name));
        Assert.Equal(["未知のタグ"], groups[^1].Tags);
    }

    [Fact]
    public void NoMatchingCategory_ReturnsSingleHeaderlessGroup()
    {
        var groups = TagCategories.Group(["Melee", "Ranged"]);

        var group = Assert.Single(groups);
        Assert.Equal(string.Empty, group.Name);
        Assert.Equal(["Melee", "Ranged"], group.Tags);
    }
}
