namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// アプリ全体の永続設定。言語(ロケール)と通知ウィンドウの表示位置。
/// </summary>
public sealed record AppSettings(string Locale, NotificationPosition NotificationPosition = NotificationPosition.BottomRight)
{
    public static AppSettings Default { get; } = new("ja-JP");
}
