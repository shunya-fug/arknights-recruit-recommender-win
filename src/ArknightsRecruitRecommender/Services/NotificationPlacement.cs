using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// 通知ウィンドウを、作業領域(モニタのタスクバーを除いた範囲)のどこに置くかの計算。
/// ゲームのあるモニタに寄せる場合、作業領域はプライマリの左や上にあるモニタでは負の座標になるため、
/// 画面系に依存しない純粋な関数にして、四隅それぞれを単体テストで確認できるようにしている。
/// </summary>
public static class NotificationPlacement
{
    /// <summary>指定した隅にウィンドウの左上を置く座標(作業領域と同じ単位)。</summary>
    public static (double Left, double Top) Calculate(
        NotificationPosition position,
        double areaLeft,
        double areaTop,
        double areaRight,
        double areaBottom,
        double windowWidth,
        double windowHeight,
        double margin)
    {
        var left = position is NotificationPosition.TopLeft or NotificationPosition.BottomLeft
            ? areaLeft + margin
            : areaRight - windowWidth - margin;

        var top = position is NotificationPosition.TopLeft or NotificationPosition.TopRight
            ? areaTop + margin
            : areaBottom - windowHeight - margin;

        return (left, top);
    }

    /// <summary>
    /// ウィンドウの最大の高さ。作業領域の高さから上下の余白を除いたもので、超えた分は
    /// 呼び出し側のスクロールで吸収する。
    /// </summary>
    public static double MaxHeight(double areaTop, double areaBottom, double margin) =>
        Math.Max(0, areaBottom - areaTop - margin * 2);
}
