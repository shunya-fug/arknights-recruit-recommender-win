namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// NotificationPositionの選択肢と日本語ラベルの対応表。トレイメニュー(App.xaml.cs)と
/// 開発補助ツール(tools/NotificationPreview)の両方から参照し、選択肢・ラベル文言を
/// 一箇所で管理する(二重定義によるラベルのズレを防ぐため)。
/// </summary>
public static class NotificationPositionLabels
{
    public static readonly (NotificationPosition Position, string Label)[] Options =
    {
        (NotificationPosition.TopLeft, "左上"),
        (NotificationPosition.TopRight, "右上"),
        (NotificationPosition.BottomLeft, "左下"),
        (NotificationPosition.BottomRight, "右下"),
    };
}
