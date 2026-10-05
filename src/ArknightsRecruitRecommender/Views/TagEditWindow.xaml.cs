using System.Windows;
using System.Windows.Input;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Views;

/// <summary>
/// OCRが誤検出・検出漏れしたタグを、ユーザーが手動で補正するための編集ウィンドウ(Issue #32)。
///
/// 通知ウィンドウとはリアルタイムに連動させず、「確定」を押した時点の選択結果だけを
/// <see cref="Confirmed"/>で1回通知する。常時監視の自動更新(数百msごと)と競合する状態を
/// 増やさないための単純な設計にしている。
/// </summary>
public partial class TagEditWindow : Window
{
    private readonly IReadOnlyList<string> _initialTags;

    /// <summary>「確定」が押されたときに、その時点で選択されていたタグの一覧を渡して発火する。</summary>
    public event Action<IReadOnlyList<string>>? Confirmed;

    public TagEditWindow(IReadOnlyList<string> knownTags, IReadOnlyList<string> initialTags)
    {
        InitializeComponent();

        _initialTags = initialTags;
        TagSelector.MaxSelectable = RecruitmentMonitorService.MaxTagsOnRecruitmentScreen;
        TagSelector.SetTags(knownTags);
        TagSelector.SetSelection(initialTags);
        TagSelector.SelectionChanged += UpdateSelectedCount;
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount() =>
        SelectedCountText.Text = $"選択中: {TagSelector.SelectedTags.Count}/{RecruitmentMonitorService.MaxTagsOnRecruitmentScreen}個";

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        TagSelector.ClearSelection();
        UpdateSelectedCount();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        TagSelector.SetSelection(_initialTags);
        UpdateSelectedCount();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(TagSelector.SelectedTags);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
