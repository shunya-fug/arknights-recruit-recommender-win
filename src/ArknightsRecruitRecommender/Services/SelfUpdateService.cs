using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// GitHub Releasesの最新版のzipをダウンロードし、実行中のexeを置き換える自己アップデート。
///
/// Windowsは実行中のexeを上書き・削除できないが、リネームはできる。そのため別の更新用
/// プロセスを用意せず、「新しいexeを隣に展開 → 実行中のexeを.oldへリネーム → 新しいexeを元の
/// パスへ配置」の順で入れ替える。置き換え後のexeは同じパスに残るので、スタートアップ登録の
/// 起動パスもずれない。
///
/// 更新用zipはアプリ自身がHTTPSで取得し、GitHubが返すSHA-256で検証してから展開する。この方法で
/// 取得・展開したファイルには、ブラウザのダウンロードやエクスプローラーでの展開で付く「インター
/// ネットから取得した」印(Mark of the Web)が付かないため、更新後の起動でSmartScreenの確認は
/// 通常出ない(最初に手動で導入したときの1回のみ)。
/// </summary>
public static class SelfUpdateService
{
    private const string ExeName = "ArknightsRecruitRecommender.exe";
    private const string OldSuffix = ".old";
    private const string NewSuffix = ".new";

    // 更新直後の後始末は、古いプロセスが終了しきるまで待ち直す。実機の検証で、終了に5秒以上
    // かかることがあったため、余裕を持って30秒(1秒間隔)まで試す。削除できた時点で終了する。
    private const int MaxCleanupAttempts = 30;

    /// <summary>
    /// 自己アップデートを実行できるか。Debugビルドや開発・テスト実行では、開発中のexeを
    /// 置き換えてしまわないよう常に不可。更新用zipの情報(URL・SHA-256)が揃っていない場合や、
    /// exeのあるフォルダに書き込めない場合も不可(呼び出し側はリリースページを開く従来の動作にする)。
    /// </summary>
    public static bool CanSelfUpdate(UpdateCheckService.UpdateInfo update, string? exePath)
    {
        if (AppDataPaths.IsLocalDevOrTest || exePath is null)
        {
            return false;
        }

        if (update.AssetDownloadUrl is null || update.AssetSha256 is null || update.AssetName is null)
        {
            return false;
        }

        if (!Uri.TryCreate(update.AssetDownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(exePath);
        return directory is not null && IsDirectoryWritable(directory);
    }

    /// <summary>
    /// 更新用zipをダウンロード・検証・展開し、実行中のexeを置き換える。置き換えが済んだ時点で戻る
    /// (再起動は呼び出し側で行う)。どこで失敗しても元のexeは使える状態のまま例外を投げる。
    /// </summary>
    /// <param name="exePath">
    /// 置き換える実行中のexeのパス。置き換え後は実行中のプロセスのモジュールのパスが.oldへ変わるため、
    /// 必ず置き換え前に取得した値を渡し、再起動にも同じ値を使うこと。
    /// </param>
    public static async Task ApplyUpdateAsync(UpdateCheckService.UpdateInfo update, string exePath, CancellationToken cancellationToken = default)
    {
        if (update.AssetDownloadUrl is null || update.AssetSha256 is null || update.AssetName is null)
        {
            throw new InvalidOperationException("更新用zipの情報が不足しています。");
        }

        var workDirectory = Path.Combine(Path.GetTempPath(), "ArknightsRecruitRecommender-update");
        Directory.CreateDirectory(workDirectory);
        var zipPath = Path.Combine(workDirectory, update.AssetName);
        var newExePath = exePath + NewSuffix;

        try
        {
            await DownloadAsync(update.AssetDownloadUrl, zipPath, cancellationToken);
            await VerifySha256Async(zipPath, update.AssetSha256, cancellationToken);

            // 展開は約190MBの書き出しになり、UIスレッドで同期実行すると数秒間画面が固まるため、
            // スレッドプールで実行する。
            await Task.Run(() =>
            {
                ExtractExe(zipPath, newExePath);
                SwapExecutable(exePath, newExePath);
            }, cancellationToken);
        }
        finally
        {
            TryDelete(zipPath);
            TryDelete(newExePath);
        }
    }

    /// <summary>
    /// 前回の更新で残った.old(置き換え前のexe)・.newを削除する。更新直後は、古いプロセスが
    /// まだ終了しきっておらず.oldを削除できないことがあるため、最大30秒待ち直す。削除できなくても
    /// 動作には影響しないので、次回起動時にもう一度試す。
    /// </summary>
    public static async Task CleanupLeftoversAsync(string? exePath)
    {
        if (exePath is null)
        {
            return;
        }

        for (var attempt = 0; attempt < MaxCleanupAttempts; attempt++)
        {
            var oldDeleted = TryDelete(exePath + OldSuffix);
            var newDeleted = TryDelete(exePath + NewSuffix);
            if (oldDeleted && newDeleted)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    public static async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        // 約75MBのため、更新確認(5秒)とは別に、HttpClient自体のタイムアウトは設けない。通信が止まった場合に
        // 戻ってこなくならないよう、呼び出し側がCancellationTokenで全体のタイムアウトをかけること。
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArknightsRecruitRecommender-SelfUpdate");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <exception cref="InvalidDataException">ファイルのSHA-256が期待値と一致しない場合。</exception>
    public static async Task VerifySha256Async(string path, string expectedHex, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ダウンロードしたファイルのSHA-256が一致しませんでした。");
        }
    }

    /// <summary>zipのルートにあるexeだけを取り出す(他のファイルは更新に使わない)。</summary>
    public static void ExtractExe(string zipPath, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(ExeName)
            ?? throw new InvalidDataException($"zip内に{ExeName}が見つかりませんでした。");
        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    /// <summary>
    /// 実行中のexeを.oldへリネームし、新しいexeを元のパスへ配置する。新しいexeの配置に失敗した
    /// 場合は、.oldを元に戻して例外を投げる(元のexeが使えない状態を残さない)。
    /// </summary>
    public static void SwapExecutable(string currentPath, string newPath)
    {
        var oldPath = currentPath + OldSuffix;

        // 前回の更新の残骸。古いプロセスが.oldを開いたままだと削除に失敗し、ここで例外になる
        // (その場合は元のexeに手を付けずに失敗として扱える)。
        if (File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        File.Move(currentPath, oldPath);
        try
        {
            File.Move(newPath, currentPath);
        }
        catch
        {
            File.Move(oldPath, currentPath);
            throw;
        }
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
