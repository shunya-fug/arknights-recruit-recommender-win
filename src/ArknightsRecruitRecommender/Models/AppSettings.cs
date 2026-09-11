namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// アプリ全体の永続設定。言語(ロケール)、通知ウィンドウの表示位置、コンパクト表示の有無。
/// </summary>
public sealed record AppSettings(
    string Locale,
    NotificationPosition NotificationPosition = NotificationPosition.BottomRight,
    bool CompactNotificationDisplay = false)
{
    public static AppSettings Default { get; } = new("ja-JP");
}
