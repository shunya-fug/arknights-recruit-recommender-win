using System.Windows;

namespace NotificationPreview;

/// <summary>
/// 開発補助ツール: 通知ウィンドウ(NotificationWindow)の表示を、実機のゲーム画面無しで
/// 対話的に確認するためのスタンドアロンアプリ。詳細は<see cref="PreviewWindow"/>参照。
/// 配布物には含まれない(ArknightsRecruitRecommender本体のみがリリース対象、
/// .github/workflows/release.ymlも本体のみをpublishする)。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.Run(new PreviewWindow());
    }
}
