using System.Windows;
using System.Windows.Controls;
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
    // 実機の公開求人画面に同時に表示されるタグは最大5個(RecruitmentMonitorService参照)のため、
    // このプレビューでも選択数をそれまでに制限する(超えるタグボタンは選択不可にする)。
    private const int MaxSelectableTags = RecruitmentMonitorService.MaxTagsOnRecruitmentScreen;

    // 「ランダム5個」ボタンで選ぶ個数(実機の画面で多いのは5個)。
    private const int RandomTagCount = 5;

    private readonly IReadOnlyList<OperatorInfo> _operators;
    private readonly IReadOnlyList<string> _knownTags;
    private readonly RecruitmentAnalyzer _analyzer = new();
    private readonly Random _random = new();

    // 通知ウィンドウのタイムライン風セレクタで選んだ募集時間の区間(本番と同じ初期値)。
    private RecruitTimeBand _recruitTimeBand = AppSettings.Default.RecruitTimeBand;
    private readonly NotificationWindow _notificationWindow = new(NotificationPosition.TopRight);

    public PreviewWindow()
    {
        InitializeComponent();

        _operators = new OperatorDataProvider("ja-JP").Load();
        _knownTags = OperatorDataProvider.GetAllKnownTags(_operators);

        TagSelector.MaxSelectable = MaxSelectableTags;
        TagSelector.SetTags(_knownTags);
        TagSelector.SelectionChanged += UpdateResults;
        _notificationWindow.BandHintProvider = (tags, band) => _analyzer.FindRarityGainInLongerBand(tags, _operators, band);
        _notificationWindow.RecruitTimeBandChanged += band =>
        {
            _recruitTimeBand = band;
            UpdateResults();
        };
        BuildPositionComboBox();
        UpdateResults();

        // このプレビュー用ウィンドウを閉じたらプロセスごと終了する。NotificationWindowは
        // ×ボタンがHide()のみ(本番の挙動を再現するため)でClose()しないため、既定の
        // OnLastWindowCloseに任せると終了しない。
        Closed += (_, _) => Application.Current.Shutdown();
    }

    /// <summary>
    /// _notificationWindowの初期位置(NotificationPosition.TopRight、フィールド初期化子参照)と
    /// 選択状態を合わせるため、"右上"を初期選択にする。
    /// </summary>
    private void BuildPositionComboBox()
    {
        foreach (var (position, label) in NotificationPositionLabels.Options)
        {
            PositionComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = position });
        }

        PositionComboBox.SelectedIndex = Array.FindIndex(NotificationPositionLabels.Options, o => o.Position == NotificationPosition.TopRight);
    }

    private void PositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PositionComboBox.SelectedItem is ComboBoxItem { Tag: NotificationPosition position })
        {
            _notificationWindow.SetPosition(position);
        }
    }

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        TagSelector.SetSelection(_knownTags.OrderBy(_ => _random.Next()).Take(RandomTagCount));
        UpdateResults();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        TagSelector.ClearSelection();
        UpdateResults();
    }

    private void CompactDisplayCheckBox_Click(object sender, RoutedEventArgs e) =>
        _notificationWindow.SetCompactDisplay(CompactDisplayCheckBox.IsChecked == true);

    private void UpdateResults()
    {
        // 選択数の上限(MaxSelectableTags)は、実機で選べない組み合わせを試せてしまわないよう
        // TagSelectorPanel側で適用している(上限に達すると未選択のタグは選べなくなる)。
        var selectedTags = TagSelector.SelectedTags;
        SelectedCountText.Text = $"選択中: {selectedTags.Count}/{MaxSelectableTags}個";

        var combinations = selectedTags.Count == 0
            ? Array.Empty<CombinationResult>()
            : _analyzer.Evaluate(selectedTags, _operators, _recruitTimeBand);

        ResultsListBox.ItemsSource = combinations.Count == 0
            ? new[] { "(該当する組み合わせなし)" }
            : combinations.Select(FormatCombination).ToList();

        // 通知ウィンドウには本番と同じく「おすすめ」だけを表示する。全組み合わせの内訳は
        // 上のResultsListBox側(このプレビュー専用)で確認する。選択中のタグをそのまま
        // 検出タグとして渡し、Issue #26 Stage 1の表示もこのツールでプレビューできるようにする。
        _notificationWindow.ShowResults(selectedTags, combinations.Where(c => c.IsRecommended).ToList());
    }

    private static string FormatCombination(CombinationResult c) =>
        $"{(c.IsRecommended ? "★" : " ")} [{string.Join(" / ", c.Tags)}] -> {c.GuaranteedMinRarity}★以上確定 ({c.MatchingOperators.Count}件)";
}
