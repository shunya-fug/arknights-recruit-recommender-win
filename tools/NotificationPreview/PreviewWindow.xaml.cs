using System.Windows;
using System.Windows.Controls.Primitives;
using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;
using ArknightsRecruitRecommender.Views;

namespace NotificationPreview;

/// <summary>
/// NotificationWindowの表示ロジックを、実機のゲーム画面無しで対話的に確認するための開発補助ツール。
/// タグをトグルボタンで選択すると、本番と同じRecruitmentAnalyzerを通した結果を、本番と同じ
/// NotificationWindowにリアルタイムで反映する。
/// </summary>
public partial class PreviewWindow : Window
{
    // 実機の公開求人画面は1枠あたり最大5個までしかタグを選べない仕様のため、このプレビューでも
    // 選択数を5個までに制限する(超えるタグボタンは選択不可にする)。ランダム選択の個数も揃える。
    private const int MaxSelectableTags = 5;

    private readonly IReadOnlyList<OperatorInfo> _operators;
    private readonly IReadOnlyList<string> _knownTags;
    private readonly RecruitmentAnalyzer _analyzer = new();
    private readonly Random _random = new();
    private readonly NotificationWindow _notificationWindow = new(NotificationPosition.TopRight);
    private readonly Dictionary<string, ToggleButton> _tagButtons = new();

    public PreviewWindow()
    {
        InitializeComponent();

        _operators = new OperatorDataProvider("ja-JP").Load();
        _knownTags = OperatorDataProvider.GetAllKnownTags(_operators);

        BuildTagButtons();
        UpdateResults();

        // このプレビュー用ウィンドウを閉じたらプロセスごと終了する。NotificationWindowは
        // ×ボタンがHide()のみ(本番の挙動を再現するため)でClose()しないため、既定の
        // OnLastWindowCloseに任せると終了しない。
        Closed += (_, _) => Application.Current.Shutdown();
    }

    private void BuildTagButtons()
    {
        var style = (Style)FindResource("TagToggleStyle");

        foreach (var tag in _knownTags)
        {
            var button = new ToggleButton
            {
                Content = tag,
                Style = style,
            };
            button.Click += (_, _) => UpdateResults();
            _tagButtons[tag] = button;
            TagsPanel.Children.Add(button);
        }
    }

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var button in _tagButtons.Values)
        {
            button.IsChecked = false;
        }

        foreach (var tag in _knownTags.OrderBy(_ => _random.Next()).Take(MaxSelectableTags))
        {
            _tagButtons[tag].IsChecked = true;
        }

        UpdateResults();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var button in _tagButtons.Values)
        {
            button.IsChecked = false;
        }

        UpdateResults();
    }

    private void CompactDisplayCheckBox_Click(object sender, RoutedEventArgs e) =>
        _notificationWindow.SetCompactDisplay(CompactDisplayCheckBox.IsChecked == true);

    private void UpdateResults()
    {
        var selectedTags = _tagButtons.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        SelectedCountText.Text = $"選択中: {selectedTags.Count}/{MaxSelectableTags}個";

        // 上限に達したら、まだ選んでいないタグのボタンをクリックできなくする(選択済みのボタンは
        // 解除できるよう有効のまま)。実機で選べない組み合わせを試せてしまわないようにするため。
        var atLimit = selectedTags.Count >= MaxSelectableTags;
        foreach (var (_, button) in _tagButtons)
        {
            button.IsEnabled = button.IsChecked == true || !atLimit;
        }

        var combinations = selectedTags.Count == 0
            ? Array.Empty<CombinationResult>()
            : _analyzer.Evaluate(selectedTags, _operators);

        ResultsListBox.ItemsSource = combinations.Count == 0
            ? new[] { "(該当する組み合わせなし)" }
            : combinations.Select(FormatCombination).ToList();

        // 通知ウィンドウには本番と同じく「おすすめ」だけを表示する。全組み合わせの内訳は
        // 上のResultsListBox側(このプレビュー専用)で確認する。
        _notificationWindow.ShowResults(combinations.Where(c => c.IsRecommended).ToList());
    }

    private static string FormatCombination(CombinationResult c) =>
        $"{(c.IsRecommended ? "★" : " ")} [{string.Join(" / ", c.Tags)}] -> {c.GuaranteedMinRarity}★以上確定 ({c.MatchingOperators.Count}件)";
}
