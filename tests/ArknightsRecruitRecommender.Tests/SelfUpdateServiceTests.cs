using System.IO.Compression;
using System.Security.Cryptography;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public sealed class SelfUpdateServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ArkSelfUpdateTests-" + Guid.NewGuid().ToString("N"));

    public SelfUpdateServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task VerifySha256_AcceptsMatchingHashCaseInsensitively_AndRejectsMismatch()
    {
        var path = Write("a.zip", "hello");
        var hex = Convert.ToHexString(SHA256.HashData("hello"u8.ToArray()));

        await SelfUpdateService.VerifySha256Async(path, hex);
        await SelfUpdateService.VerifySha256Async(path, hex.ToLowerInvariant());
        await Assert.ThrowsAsync<InvalidDataException>(() => SelfUpdateService.VerifySha256Async(path, new string('0', 64)));
    }

    [Fact]
    public void ExtractExe_ExtractsOnlyTheRootExe_AndFailsWhenMissing()
    {
        var zipPath = Path.Combine(_dir, "release.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "ArknightsRecruitRecommender.exe", "new-exe");
            WriteEntry(zip, "ArknightsRecruitRecommender.pdb", "symbols");
        }

        var dest = Path.Combine(_dir, "out.exe");
        SelfUpdateService.ExtractExe(zipPath, dest);
        Assert.Equal("new-exe", File.ReadAllText(dest));

        var noExeZip = Path.Combine(_dir, "noexe.zip");
        using (var zip = ZipFile.Open(noExeZip, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "readme.txt", "x");
        }

        Assert.Throws<InvalidDataException>(() => SelfUpdateService.ExtractExe(noExeZip, Path.Combine(_dir, "none.exe")));
    }

    [Fact]
    public void SwapExecutable_ReplacesTheExe_KeepingTheOldOneAsDotOld()
    {
        var current = Write("app.exe", "old-exe");
        var next = Write("app.exe.new", "new-exe");

        SelfUpdateService.SwapExecutable(current, next);

        Assert.Equal("new-exe", File.ReadAllText(current));
        Assert.Equal("old-exe", File.ReadAllText(current + ".old"));
        Assert.False(File.Exists(next));
    }

    [Fact]
    public void SwapExecutable_RollsBackWhenTheNewExeCannotBePlaced()
    {
        var current = Write("app.exe", "old-exe");

        // 新しいexeが存在しない=配置に失敗するケース。元のexeが使える状態に戻ること。
        Assert.ThrowsAny<IOException>(() => SelfUpdateService.SwapExecutable(current, Path.Combine(_dir, "missing.new")));

        Assert.Equal("old-exe", File.ReadAllText(current));
    }

    [Fact]
    public void SwapExecutable_OverwritesALeftoverDotOldFromAPreviousUpdate()
    {
        var current = Write("app.exe", "v2");
        Write("app.exe.old", "v1-leftover");
        var next = Write("app.exe.new", "v3");

        SelfUpdateService.SwapExecutable(current, next);

        Assert.Equal("v3", File.ReadAllText(current));
        Assert.Equal("v2", File.ReadAllText(current + ".old"));
    }

    [Fact]
    public async Task CleanupLeftovers_DeletesDotOldAndDotNew()
    {
        var current = Write("app.exe", "cur");
        Write("app.exe.old", "o");
        Write("app.exe.new", "n");

        await SelfUpdateService.CleanupLeftoversAsync(current);

        Assert.False(File.Exists(current + ".old"));
        Assert.False(File.Exists(current + ".new"));
        Assert.True(File.Exists(current));
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
