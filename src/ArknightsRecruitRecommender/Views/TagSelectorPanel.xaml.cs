using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Views;

/// <summary>
/// 全タグを分類(<see cref="TagCategories"/>)ごとにまとめたトグルボタンの一覧。
/// タグ編集ウィンドウ(<see cref="TagEditWindow"/>)と開発補助ツール(NotificationPreview)で、
/// 同じタグ選択UIを重複実装せずに共有するための部品。
/// </summary>
public partial class TagSelectorPanel : UserControl
{
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5));

    private readonly Dictionary<string, ToggleButton> _buttons = new();
    private IReadOnlyList<string> _knownTags = Array.Empty<string>();

    /// <summary>
    /// 選択できるタグ数の上限。nullなら無制限。上限に達すると、未選択のタグは選べなくなる
    /// (選択済みのタグは解除できる)。
    /// </summary>
    public int? MaxSelectable { get; set; }

    /// <summary>ユーザーがタグボタンをクリックして選択状態を変えた時に発火する(コードからの変更では発火しない)。</summary>
    public event Action? SelectionChanged;

    /// <summary>選択中のタグ。並びは<see cref="SetTags"/>に渡した既知タグの並び順。</summary>
    public IReadOnlyList<string> SelectedTags =>
        _knownTags.Where(tag => _buttons[tag].IsChecked == true).ToList();

    public TagSelectorPanel()
    {
        InitializeComponent();
    }

    public void SetTags(IReadOnlyList<string> knownTags)
    {
        _knownTags = knownTags;
        _buttons.Clear();
        GroupsPanel.Children.Clear();

        var style = (Style)FindResource("TagToggleStyle");
        var groups = TagCategories.Group(knownTags);

        foreach (var group in groups)
        {
            if (group.Name.Length > 0)
            {
                GroupsPanel.Children.Add(new TextBlock
                {
                    Text = group.Name,
                    Foreground = HeaderBrush,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }

            var wrap = new WrapPanel();
            foreach (var tag in group.Tags)
            {
                var button = new ToggleButton { Content = tag, Style = style };
                button.Click += (_, _) =>
                {
                    ApplyLimit();
                    SelectionChanged?.Invoke();
                };
                _buttons[tag] = button;
                wrap.Children.Add(button);
            }

            GroupsPanel.Children.Add(wrap);
        }
    }

    /// <summary>選択状態を、指定したタグだけがチェックされた状態にする。未知のタグは無視する。</summary>
    public void SetSelection(IEnumerable<string> tags)
    {
        var selected = new HashSet<string>(tags);
        foreach (var (tag, button) in _buttons)
        {
            button.IsChecked = selected.Contains(tag);
        }

        ApplyLimit();
    }

    public void ClearSelection() => SetSelection(Array.Empty<string>());

    private void ApplyLimit()
    {
        var atLimit = MaxSelectable is { } max && _buttons.Values.Count(b => b.IsChecked == true) >= max;
        foreach (var button in _buttons.Values)
        {
            button.IsEnabled = button.IsChecked == true || !atLimit;
        }
    }
}
