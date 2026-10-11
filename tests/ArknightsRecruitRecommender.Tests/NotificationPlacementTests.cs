using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public class NotificationPlacementTests
{
    private const double Margin = 16;

    // 作業領域(1920x1040、タスクバー分だけ高さが引かれている)と、通知ウィンドウ(380x200)。
    [Theory]
    [InlineData(NotificationPosition.TopLeft, 0, 0, 16, 16)]
    [InlineData(NotificationPosition.TopRight, 0, 0, 1524, 16)]
    [InlineData(NotificationPosition.BottomLeft, 0, 0, 16, 824)]
    [InlineData(NotificationPosition.BottomRight, 0, 0, 1524, 824)]
    // プライマリの左・上に並んだモニタは作業領域の原点が負になる。四隅とも、その原点を基準に置かれること。
    [InlineData(NotificationPosition.TopLeft, -1920, -1080, -1904, -1064)]
    [InlineData(NotificationPosition.TopRight, -1920, -1080, -396, -1064)]
    [InlineData(NotificationPosition.BottomLeft, -1920, -1080, -1904, -256)]
    [InlineData(NotificationPosition.BottomRight, -1920, -1080, -396, -256)]
    public void Calculate_PlacesTheWindowInTheCornerOfTheWorkArea(
        NotificationPosition position, double areaLeft, double areaTop, double expectedLeft, double expectedTop)
    {
        var (left, top) = NotificationPlacement.Calculate(
            position, areaLeft, areaTop, areaLeft + 1920, areaTop + 1040, windowWidth: 380, windowHeight: 200, Margin);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Theory]
    [InlineData(0, 1040, 1008)]
    [InlineData(-1080, -40, 1008)]
    // 作業領域が余白より小さくても負の高さにはならない。
    [InlineData(0, 20, 0)]
    public void MaxHeight_LeavesTheMarginAboveAndBelow(double areaTop, double areaBottom, double expected)
    {
        Assert.Equal(expected, NotificationPlacement.MaxHeight(areaTop, areaBottom, Margin));
    }
}
