namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// アプリ全体の永続設定。現状は言語(ロケール)のみ。
/// </summary>
public sealed record AppSettings(string Locale)
{
    public static AppSettings Default { get; } = new("ja-JP");
}
