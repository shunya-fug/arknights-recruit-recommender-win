namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// 「Windows起動時に自動起動」の登録(HKCU\...\Run)。AppSettingsには独自の状態を持たず、
/// レジストリの実際の値そのものを正とする(タスクマネージャーのスタートアップタブ等から
/// 直接変更された場合にもズレないようにするため)。
/// </summary>
internal static class StartupRegistration
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // AppDataPaths.RootDirectoryと同様、Debug構成/テストホストからの実行では別の値名を使う。
    // 同じ値名のままだと、開発者がdotnet run/dotnet test経由でこのトグルをローカル検証した際、
    // bin/Debug配下の一時的なビルドパスがHKCU\...\Runに永続登録されてしまい、実機のWindows
    // ログイン時にそのビルドを起動しようとし続ける(削除後は起動に失敗する)不具合につながる。
    private static readonly string ValueName =
        AppDataPaths.IsLocalDevOrTest ? "ArknightsRecruitRecommender.Debug" : "ArknightsRecruitRecommender";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[スタートアップ登録] 確認に失敗しました: {ex}");
            return false;
        }
    }

    public static void SetRegistered(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (exePath is not null)
                {
                    key.SetValue(ValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"[スタートアップ登録] 変更に失敗しました: {ex}");
        }
    }

    /// <summary>
    /// 自動起動が既に有効な場合、レジストリの起動パスを現在実行中のexeのパスへ上書きする
    /// (自己修復)。このアプリはインストーラを持たず、リリースのたびに別フォルダへ展開する
    /// 運用のため、パスをexe移動・バージョン更新のたびに手動で登録し直す必要がある(登録済み
    /// フラグはレジストリの値の有無だけで判定しているため、古いパスのままでも「有効」に
    /// 見えてしまい気づきにくい)。アプリを起動するたびにこの処理を通すことで、ユーザーが
    /// 移動先・新バージョンを一度でも手動起動すれば古いパス情報が残り続けることはない。
    /// (一度も手動起動しないままexeを移動・削除した場合は対象外。この処理自体が実行されない
    /// ため)。
    /// </summary>
    public static void RefreshPathIfRegistered()
    {
        if (IsRegistered())
        {
            SetRegistered(true);
        }
    }
}
