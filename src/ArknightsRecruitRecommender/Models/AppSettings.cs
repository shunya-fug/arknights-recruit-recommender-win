namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// アプリ全体の永続設定。言語(ロケール)、通知ウィンドウの表示位置、コンパクト表示の有無、
/// 想定する募集時間の区間。
/// </summary>
public sealed record AppSettings(
    string Locale,
    NotificationPosition NotificationPosition = NotificationPosition.BottomRight,
    bool CompactNotificationDisplay = false,
    RecruitTimeBand RecruitTimeBand = RecruitTimeBand.From740)
{
    public static AppSettings Default { get; } = new("ja-JP");
}
