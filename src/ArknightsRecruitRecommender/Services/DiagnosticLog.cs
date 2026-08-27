using System.IO;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// 実機での動作確認時、GUIのダイアログ/通知ウィンドウが一瞬で消えてしまい内容を確認できない
/// 場合があるため、主要なイベントをファイルに追記しておく簡易ロガー。
/// %LOCALAPPDATA%\ArknightsRecruitRecommender\diagnostic.log に書き出す。
/// </summary>
public static class DiagnosticLog
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArknightsRecruitRecommender",
        "diagnostic.log");

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFilePath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // ログ出力自体の失敗でアプリの動作に影響を与えないようにする。
        }
    }
}
