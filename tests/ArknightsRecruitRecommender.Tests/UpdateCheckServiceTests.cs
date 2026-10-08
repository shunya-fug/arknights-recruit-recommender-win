using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public class UpdateCheckServiceTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v0.1.16",
          "html_url": "https://github.com/example/repo/releases/tag/v0.1.16",
          "assets": [
            {
              "name": "notes.txt",
              "browser_download_url": "https://example.com/notes.txt",
              "size": 10
            },
            {
              "name": "ArknightsRecruitRecommender-v0.1.16-win-x64.zip",
              "browser_download_url": "https://example.com/ArknightsRecruitRecommender-v0.1.16-win-x64.zip",
              "size": 74830206,
              "digest": "sha256:5CB47A6FD8298799368B72126BD984676931196883C9376D971387B952623476"
            }
          ]
        }
        """;

    [Fact]
    public void NewerRelease_ReturnsVersionAndZipAssetInfo()
    {
        var info = UpdateCheckService.ParseLatestRelease(ReleaseJson, new Version(0, 1, 15, 0));

        Assert.NotNull(info);
        Assert.Equal(new Version(0, 1, 16), info.LatestVersion);
        Assert.Equal("ArknightsRecruitRecommender-v0.1.16-win-x64.zip", info.AssetName);
        Assert.Equal("https://example.com/ArknightsRecruitRecommender-v0.1.16-win-x64.zip", info.AssetDownloadUrl);
        Assert.Equal(74830206, info.AssetSizeBytes);
        Assert.Equal("5CB47A6FD8298799368B72126BD984676931196883C9376D971387B952623476", info.AssetSha256);
    }

    [Theory]
    [InlineData(0, 1, 16, 0)]   // 同一バージョン(4桁のアセンブリ版数との比較でも新しいと誤判定しない)
    [InlineData(0, 1, 17, 0)]   // より新しい版を実行中
    public void SameOrOlderRelease_ReturnsNull(int major, int minor, int build, int revision)
    {
        Assert.Null(UpdateCheckService.ParseLatestRelease(ReleaseJson, new Version(major, minor, build, revision)));
    }

    [Fact]
    public void ReleaseWithoutDigest_HasNoSha256_SoSelfUpdateIsNotPossible()
    {
        var withoutDigest = """
            {
              "tag_name": "v0.1.16",
              "html_url": "https://github.com/example/repo/releases/tag/v0.1.16",
              "assets": [
                { "name": "ArknightsRecruitRecommender-v0.1.16-win-x64.zip", "browser_download_url": "https://example.com/a.zip", "size": 1 }
              ]
            }
            """;

        var info = UpdateCheckService.ParseLatestRelease(withoutDigest, new Version(0, 1, 15, 0));

        Assert.NotNull(info);
        Assert.Null(info.AssetSha256);
        // 整合性を検証できない更新は自己アップデートの対象にしない(リリースページを開く動作になる)。
        Assert.False(SelfUpdateService.CanSelfUpdate(info, @"C:\app\ArknightsRecruitRecommender.exe"));
    }

    [Fact]
    public void ReleaseWithoutZipAsset_ReturnsInfoWithoutAsset()
    {
        var json = """{ "tag_name": "v0.1.16", "html_url": "https://example.com/r", "assets": [] }""";

        var info = UpdateCheckService.ParseLatestRelease(json, new Version(0, 1, 15, 0));

        Assert.NotNull(info);
        Assert.Null(info.AssetDownloadUrl);
    }
}
