using System.Windows;
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
