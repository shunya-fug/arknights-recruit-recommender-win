using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Tests;

public class RecruitTimeBandTests
{
    [Theory]
    [InlineData(RecruitTimeBand.UpTo350, 1, true)]
    [InlineData(RecruitTimeBand.UpTo350, 4, true)]
    [InlineData(RecruitTimeBand.UpTo350, 5, false)]
    [InlineData(RecruitTimeBand.From400To730, 1, false)]
    [InlineData(RecruitTimeBand.From400To730, 2, true)]
    [InlineData(RecruitTimeBand.From400To730, 5, true)]
    [InlineData(RecruitTimeBand.From400To730, 6, false)]
    [InlineData(RecruitTimeBand.From740, 2, false)]
    [InlineData(RecruitTimeBand.From740, 3, true)]
    [InlineData(RecruitTimeBand.From740, 6, true)]
    public void AllowsRarity_FollowsTheGameRecruitRange(RecruitTimeBand band, int rarity, bool expected)
    {
        Assert.Equal(expected, band.AllowsRarity(rarity));
    }

    [Fact]
    public void DefaultSettings_UseTheNineHourBand()
    {
        Assert.Equal(RecruitTimeBand.From740, AppSettings.Default.RecruitTimeBand);
    }
}
