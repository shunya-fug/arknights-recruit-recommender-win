using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Tests;

public class RarityLabelsTests
{
    [Theory]
    [InlineData(1, "★１")]
    [InlineData(6, "★６")]
    public void Star_UsesFullWidthDigits(int rarity, string expected)
    {
        Assert.Equal(expected, RarityLabels.Star(rarity));
    }

    [Theory]
    [InlineData(4, "★４以上確定")]
    [InlineData(5, "★５以上確定")]
    // ★６は上限なので「以上」を付けない。
    [InlineData(6, "★６確定")]
    public void Guarantee_OmitsOrMoreForTheTopRarity(int rarity, string expected)
    {
        Assert.Equal(expected, RarityLabels.Guarantee(rarity));
    }
}
