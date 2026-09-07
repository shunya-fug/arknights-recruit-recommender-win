using System.Windows;
using System.Windows.Media;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Views;

public partial class NotificationWindow : Window
{
    // ウィンドウ端からの余白(ピクセル)。4隅どのプリセットでも揃った見た目にするため共通で使う。
    private const double ScreenMargin = 16;

    // ウィンドウはSizeToContent="WidthAndHeight"で、コンストラクタ時点ではActualWidth/Height
    // がまだ0(未レイアウト)のため、初回配置はこの概算値で行う。実際のサイズが確定した時点で
    // SizeChangedにより正確な位置へ補正される。
    private const double InitialWidthEstimate = 380;
    private const double InitialHeightEstimate = 200;

    // 対象オペレーターが多い組み合わせ(例:★4以上確定のプール)で通知が縦に伸びすぎないよう、
    // 名前の列挙はここまでにして残りは「他N名」とまとめる。
    private const int MaxOperatorNamesShown = 6;

    private NotificationPosition _position;

    public NotificationWindow(NotificationPosition position)
    {
        InitializeComponent();

        _position = position;
        SizeChanged += (_, _) => ApplyPosition();
        ApplyPosition();
    }

    /// <summary>
    /// 表示位置プリセットを変更し、即座に反映する(トレイメニューからの変更を再起動不要で
    /// 反映するため)。
    /// </summary>
    public void SetPosition(NotificationPosition position)
    {
        _position = position;
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : InitialWidthEstimate;
        var height = ActualHeight > 0 ? ActualHeight : InitialHeightEstimate;

        Left = _position is NotificationPosition.TopLeft or NotificationPosition.BottomLeft
            ? workArea.Left + ScreenMargin
            : workArea.Right - width - ScreenMargin;

        Top = _position is NotificationPosition.TopLeft or NotificationPosition.TopRight
            ? workArea.Top + ScreenMargin
            : workArea.Bottom - height - ScreenMargin;
    }

    /// <summary>
    /// おすすめの組み合わせが0件の場合も明示的に「無し」と表示する。判定中(まだ何も
    /// 表示されていない)状態と区別できるようにするため。
    /// </summary>
    public void ShowResults(IReadOnlyList<CombinationResult> results)
    {
        TitleText.Text = "★4以上 確定タグの組み合わせ";
        DebugSummaryText.Visibility = Visibility.Collapsed;
        RenderResults(results);
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
        DebugSummaryText.Text = result.MatchedTags.Count == 0
            ? "検出タグ: (一致なし)"
            : $"検出タグ: {string.Join(" / ", result.MatchedTags)}";
        DebugSummaryText.Visibility = Visibility.Visible;
        RenderResults(result.Combinations.Where(r => r.IsRecommended).ToList());
        Show();
    }

    private void RenderResults(IReadOnlyList<CombinationResult> results)
    {
        if (results.Count == 0)
        {
            NoResultsText.Visibility = Visibility.Visible;
            ResultsList.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            return;
        }

        NoResultsText.Visibility = Visibility.Collapsed;
        ResultsList.Visibility = Visibility.Visible;
        ResultsList.ItemsSource = results.Select(ToDisplayItem).ToList();
    }

    private static CombinationDisplayItem ToDisplayItem(CombinationResult r)
    {
        var names = r.MatchingOperators.Select(o => o.Name).ToList();
        var operatorsText = names.Count <= MaxOperatorNamesShown
            ? string.Join(" / ", names)
            : string.Join(" / ", names.Take(MaxOperatorNamesShown)) + $" 他{names.Count - MaxOperatorNamesShown}名";

        return new CombinationDisplayItem(
            TagsText: $"タグ: {string.Join(" / ", r.Tags)}",
            RarityText: $"{r.GuaranteedMinRarity}★以上確定 ({r.MatchingOperators.Count}件)",
            RarityBrush: RarityBrush(r.GuaranteedMinRarity),
            OperatorsText: operatorsText);
    }

    // アークナイツ本編のレアリティ配色に合わせた背景色。GuaranteedMinRarityは「この組み合わせで
    // 保証される最低レアリティ」であり、通知対象(IsRecommended)は常に4以上なので3以下は
    // 実際には出現しないが、念のためフォールバックを用意している。
    private static readonly SolidColorBrush Rarity6Brush = Freeze(0xFF, 0x9A, 0x2E);
    private static readonly SolidColorBrush Rarity5Brush = Freeze(0xFF, 0xD5, 0x4F);
    private static readonly SolidColorBrush Rarity4Brush = Freeze(0xC0, 0x92, 0xFF);
    private static readonly SolidColorBrush FallbackRarityBrush = Freeze(0x9E, 0x9E, 0x9E);

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush RarityBrush(int? rarity) => rarity switch
    {
        6 => Rarity6Brush,
        5 => Rarity5Brush,
        4 => Rarity4Brush,
        _ => FallbackRarityBrush,
    };

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    /// <summary>
    /// 通知1件分の表示用データ。ドメインモデル(<see cref="CombinationResult"/>)をそのまま
    /// バインドせず、ここで整形済みの文字列・色を持たせることでXAML側のテンプレートを単純に保つ。
    /// </summary>
    private sealed record CombinationDisplayItem(
        string TagsText,
        string RarityText,
        SolidColorBrush RarityBrush,
        string OperatorsText);
}
