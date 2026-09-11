using System.IO;
using System.Reflection;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// アプリの永続データ(設定・診断ログ)を保存するフォルダ。
///
/// Debugビルド、および(構成に関わらず)テストホストからの実行では、配布用Releaseビルドとは
/// 別フォルダを使う。開発者自身が実機ユーザーでもある場合、両者が同じ%LOCALAPPDATA%配下の
/// 同名フォルダを使っていると、ローカルでのテスト実行が実機にインストール済みの本番設定・
/// ログを書き換えてしまう(実際に、テストのフィクスチャがログに混入し調査が紛らわしくなる
/// 事例があった)。DEBUG構成かどうかだけで判定すると、`dotnet test --configuration Release`
/// のようにテストをRelease構成で実行した場合に取りこぼすため、エントリアセンブリ名
/// (テストホストでは本アプリ自身の名前と異なる)もあわせて見る。
/// なお、動作確認のために本アプリ自体をRelease構成でローカル再ビルドして実行する場合は
/// この判定では区別できない(その場合は手動で%LOCALAPPDATA%の設定ファイルをバックアップ
/// すること)。
/// </summary>
internal static class AppDataPaths
{
    private static readonly bool IsLocalDevOrTest =
#if DEBUG
        true;
#else
        Assembly.GetEntryAssembly()?.GetName().Name != "ArknightsRecruitRecommender";
#endif

    public static readonly string RootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        IsLocalDevOrTest ? "ArknightsRecruitRecommender.Debug" : "ArknightsRecruitRecommender");
}
