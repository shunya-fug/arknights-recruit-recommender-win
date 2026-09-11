namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// アプリ全体の永続設定。言語(ロケール)、通知ウィンドウの表示位置、コンパクト表示の有無、
/// 更新通知を確認済みのバージョン。
/// </summary>
public sealed record AppSettings(
    string Locale,
    NotificationPosition NotificationPosition = NotificationPosition.BottomRight,
    bool CompactNotificationDisplay = false,
    // 起動のたびに同じバージョンの更新通知を出し続けないよう、直近に表示したバージョンを
    // 記録しておく(UpdateCheckService参照)。より新しいバージョンが出れば改めて通知する。
    string? AcknowledgedUpdateVersion = null)
{
    public static AppSettings Default { get; } = new("ja-JP");
}
