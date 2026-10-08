using System.Net.Http;
using System.Text.Json;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// GitHub Releasesの最新版を確認し、実行中のバージョンより新しいものがあれば知らせる(Issue #27)。
/// 更新用のzip(ダウンロードURL・サイズ・SHA-256)の情報も合わせて返し、<see cref="SelfUpdateService"/>が
/// 自己アップデートに使う。ネットワークエラー・APIレート制限等で確認自体に失敗しても、呼び出し側で
/// 通常起動を妨げないよう握りつぶすこと(このサービス自体は例外を投げうる)。
/// </summary>
public static class UpdateCheckService
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/shunya-fug/arknights-recruit-recommender-win/releases/latest";

    // リリースのzip(release.ymlで "ArknightsRecruitRecommender-<タグ>-win-x64.zip" として作成)。
    private const string AssetNameSuffix = "-win-x64.zip";

    /// <param name="AssetName">更新用zipのファイル名。見つからない場合はnull。</param>
    /// <param name="AssetDownloadUrl">更新用zipのダウンロードURL(https)。</param>
    /// <param name="AssetSizeBytes">更新用zipのサイズ(バイト)。</param>
    /// <param name="AssetSha256">更新用zipのSHA-256(16進、"sha256:"接頭辞なし)。GitHubが返さない場合はnull。</param>
    public sealed record UpdateInfo(
        Version LatestVersion,
        string HtmlUrl,
        string? AssetName = null,
        string? AssetDownloadUrl = null,
        long? AssetSizeBytes = null,
        string? AssetSha256 = null);

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

        var json = await response.Content.ReadAsStringAsync();
        return ParseLatestRelease(json, currentVersion);
    }

    /// <summary>
    /// 最新リリースのAPIレスポンス(JSON)から、引数のバージョンより新しい場合の更新情報を作る。
    /// ネットワークを介さず単体でテストできるよう、CheckForUpdateAsyncから切り出している。
    /// </summary>
    public static UpdateInfo? ParseLatestRelease(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString();
        var htmlUrl = root.GetProperty("html_url").GetString();
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

        if (latestVersion <= normalizedCurrent)
        {
            return null;
        }

        var info = new UpdateInfo(latestVersion, htmlUrl);
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null || !name.EndsWith(AssetNameSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                long? size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sizeValue) ? sizeValue : null;
                var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

                // digestは"sha256:<16進>"形式。それ以外の形式なら、検証に使えないので無いものとして扱う。
                const string DigestPrefix = "sha256:";
                var sha256 = digest is not null && digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase)
                    ? digest[DigestPrefix.Length..]
                    : null;

                return info with { AssetName = name, AssetDownloadUrl = url, AssetSizeBytes = size, AssetSha256 = sha256 };
            }
        }

        return info;
    }
}
