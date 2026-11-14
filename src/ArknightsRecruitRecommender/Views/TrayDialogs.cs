using System.Windows;

namespace ArknightsRecruitRecommender.Views;

/// <summary>トレイメニューの操作から表示するダイアログ。</summary>
internal static class TrayDialogs
{
    /// <summary>
    /// トレイのコンテキストメニュー項目のクリック直後にMessageBox.Showを呼ぶと、メニューを
    /// 閉じたクリックの入力メッセージがそのまま新しいダイアログに流れ込み、表示直後に自動的に
    /// 閉じてしまう不具合を実機で確認した(表示から149ms後に閉じており、人がクリックできる
    /// 速さではなかった)。入力キューが落ち着くまで少し待ってから表示することで回避する。
    /// メニュー項目のクリックハンドラから表示するダイアログは、必ずこれ経由で表示すること。
    /// </summary>
    public static async Task<MessageBoxResult> ShowMessageBoxAsync(
        string text, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        await Task.Delay(200);
        return MessageBox.Show(text, caption, button, icon);
    }
}
