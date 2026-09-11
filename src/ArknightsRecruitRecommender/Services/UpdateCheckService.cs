using System.Net.Http;
using System.Text.Json;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// 起動時にGitHub Releasesの最新版を確認し、実行中のバージョンより新しいものがあれば知らせる。
/// ダウンロード・自動更新は行わず、リリースページのURLを提示するだけに留める(Issue #27)。
/// ネットワークエラー・APIレート制限等で確認自体に失敗しても、呼び出し側で通常起動を
/// 妨げないよう握りつぶすこと(このサービス自体は例外を投げうる)。
/// </summary>
public static class UpdateCheckService
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/shunya-fug/arknights-recruit-recommender-win/releases/latest";

    public sealed record UpdateInfo(Version LatestVersion, string HtmlUrl);

    /// <summary>
    /// 最新リリースが引数のバージョンより新しい場合にその情報を返す。同一・古い場合はnull。
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(Version currentVersion)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // GitHub APIはUser-Agentヘッダが無いリクエストを拒否するため必須。
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"ArknightsRecruitRecommender/{currentVersion}");

        using var response = await client.GetAsync(LatestReleaseApiUrl);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        var tagName = document.RootElement.GetProperty("tag_name").GetString();
        var htmlUrl = document.RootElement.GetProperty("html_url").GetString();
        if (tagName is null || htmlUrl is null)
        {
            return null;
        }

        // タグは"v0.1.9"形式("v"接頭辞)、System.Versionは接頭辞を受け付けないため除去する。
        var versionText = tagName.StartsWith('v') ? tagName[1..] : tagName;
        if (!Version.TryParse(versionText, out var latestVersion))
        {
            return null;
        }

        // リリースタグは"0.1.9"のような3桁だが、アセンブリのバージョンは常に4桁(末尾0埋め)。
        // 4桁のまま比較すると、3桁指定側のRevisionが既定値-1のままになり、同一バージョン
        // なのに大小関係が生じてしまう(例: "0.1.9"→(0,1,9,-1) と (0,1,9,0) は本来同値なのに
        // -1 < 0 で前者が「古い」と判定される)。桁数を3桁に揃えてから比較する。
        var normalizedCurrent = new Version(
            currentVersion.Major,
            currentVersion.Minor,
            Math.Max(currentVersion.Build, 0));

        return latestVersion > normalizedCurrent ? new UpdateInfo(latestVersion, htmlUrl) : null;
    }
}
