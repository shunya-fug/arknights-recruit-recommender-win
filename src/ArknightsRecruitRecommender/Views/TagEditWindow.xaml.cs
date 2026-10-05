using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
    private readonly Dictionary<string, ToggleButton> _tagButtons = new();

    /// <summary>「確定」が押されたときに、その時点で選択されていたタグの一覧を渡して発火する。</summary>
    public event Action<IReadOnlyList<string>>? Confirmed;

    public TagEditWindow(IReadOnlyList<string> knownTags, IReadOnlyList<string> initialTags)
    {
        InitializeComponent();

        var initialTagSet = new HashSet<string>(initialTags);
        var style = (Style)FindResource("TagToggleStyle");

        foreach (var tag in knownTags)
        {
            var button = new ToggleButton
            {
                Content = tag,
                Style = style,
                IsChecked = initialTagSet.Contains(tag),
            };
            _tagButtons[tag] = button;
            TagsPanel.Children.Add(button);
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedTags = _tagButtons.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        Confirmed?.Invoke(selectedTags);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
