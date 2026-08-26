using System.Windows;
using System.Windows.Threading;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Views;

public partial class NotificationWindow : Window
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(12);
    private readonly DispatcherTimer _autoHideTimer;

    public NotificationWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer { Interval = AutoHideDelay };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            Hide();
        };

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 380;
        Top = workArea.Bottom - 200;
    }

    public void ShowResults(IReadOnlyList<CombinationResult> results)
    {
        TitleText.Text = "★4以上 確定タグの組み合わせ";
        ResultsList.ItemsSource = results.Select(FormatCombination);
        Display();
    }

    /// <summary>
    /// Manual check entry point (tray menu "手動チェック実行"): shows every detected tag and
    /// combination, not just the recommended ones, so the pipeline's behavior is visible even
    /// when nothing good was found.
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
        lines.AddRange(result.Combinations.Select(FormatCombination));

        ResultsList.ItemsSource = lines;
        Display();
    }

    private static string FormatCombination(CombinationResult r)
    {
        var marker = r.IsRecommended ? "[おすすめ] " : "";
        return $"{marker}[{string.Join(" / ", r.Tags)}] -> {r.GuaranteedMinRarity}★以上確定 ({r.MatchingOperators.Count}件)";
    }

    private void Display()
    {
        Show();
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }
}
