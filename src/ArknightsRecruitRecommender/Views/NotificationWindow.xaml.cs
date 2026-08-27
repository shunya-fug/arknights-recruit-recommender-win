using System.Windows;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Views;

public partial class NotificationWindow : Window
{
    public NotificationWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 380;
        Top = workArea.Bottom - 200;
    }

    public void ShowResults(IReadOnlyList<CombinationResult> results)
    {
        TitleText.Text = "★4以上 確定タグの組み合わせ";
        ResultsList.ItemsSource = results.Select(FormatCombination);
        Show();
    }

    /// <summary>
    /// おすすめ組み合わせの算出はタグさえ正しく検出できれば決まる静的なロジックのため、
    /// 手動チェックでも通常の自動検出(<see cref="ShowResults"/>)と同じ「おすすめのみ」を表示する。
    /// 検出タグ一覧だけは、OCR・照合の動作確認のために全件表示する。
    /// </summary>
    public void ShowDebugResult(RecruitmentCheckResult result)
    {
        TitleText.Text = "手動チェック結果";

        var lines = new List<string>
        {
            result.MatchedTags.Count == 0
                ? "検出タグ: (一致なし)"
                : $"検出タグ: {string.Join(" / ", result.MatchedTags)}",
        };
        lines.AddRange(result.Combinations.Where(r => r.IsRecommended).Select(FormatCombination));

        ResultsList.ItemsSource = lines;
        Show();
    }

    private static string FormatCombination(CombinationResult r) =>
        $"[{string.Join(" / ", r.Tags)}] -> {r.GuaranteedMinRarity}★以上確定 ({r.MatchingOperators.Count}件)";

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
