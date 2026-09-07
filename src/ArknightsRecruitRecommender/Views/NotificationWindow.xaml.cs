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

    // 「N★以上確定」ラベルを出すか(通常表示)、枠線の色と件数だけにするか(コンパクト表示)。
    // トレイメニューから切り替え可能(SetCompactDisplay参照)。
    private bool _compactDisplay;

    // コンパクト表示の切り替え時に即座に再描画するため、直近に表示した組み合わせを保持しておく。
    private IReadOnlyList<CombinationResult>? _lastResults;

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

    /// <summary>
    /// コンパクト表示のON/OFFを切り替え、表示中の内容があれば即座に反映する
    /// (表示位置設定と同様、トレイメニューからの変更に再起動を不要にするため)。
    /// </summary>
    public void SetCompactDisplay(bool compactDisplay)
    {
        _compactDisplay = compactDisplay;
        if (_lastResults is not null)
        {
            RenderResults(_lastResults);
        }
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
        TitleText.Text = "おすすめタグ一覧";
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
        _lastResults = results;

        if (results.Count == 0)
        {
            NoResultsText.Visibility = Visibility.Visible;
            ResultsList.Visibility = Visibility.Collapsed;
            ResultsList.ItemsSource = null;
            return;
        }

        NoResultsText.Visibility = Visibility.Collapsed;
        ResultsList.Visibility = Visibility.Visible;
        ResultsList.ItemsSource = results.Select(r => ToDisplayItem(r, _compactDisplay)).ToList();
    }

    private static CombinationDisplayItem ToDisplayItem(CombinationResult r, bool compact)
    {
        // 「4★以上確定」のような組み合わせでも、実際には5★・6★のオペレーターが混ざって
        // 対象になることがある(GuaranteedMinRarityはあくまで下限)。基本はバッジ表示の
        // レアリティが出る、という読み方に合わせてレアリティ昇順で並べる(ユーザー確認済み)。
        var sortedOperators = r.MatchingOperators.OrderBy(o => o.Rarity).ThenBy(o => o.Name).ToList();

        var chips = sortedOperators
            .Take(MaxOperatorNamesShown)
            .Select(o => new OperatorChip(o.Name, TintBrush(o.Rarity)))
            .ToList();

        var overflowCount = sortedOperators.Count - MaxOperatorNamesShown;
        if (overflowCount > 0)
        {
            chips.Add(new OperatorChip($"他{overflowCount}名", FallbackTintBrush));
        }

        // チップが省略されている場合に限らず、常に全対象者をホバーで確認できるようにする
        // (名前+レアリティを列形式で見せるため、単純な文字列ではなく行データとして保持する)。
        var tooltipRows = sortedOperators
            .Select(o => new OperatorTooltipRow(o.Name, $"{o.Rarity}★", RarityBrush(o.Rarity)))
            .ToList();

        return new CombinationDisplayItem(
            TagsText: $"タグ: {string.Join(" / ", r.Tags)}",
            RarityText: $"{r.GuaranteedMinRarity}★以上確定",
            CountText: $"{r.MatchingOperators.Count}件",
            RarityBrush: RarityBrush(r.GuaranteedMinRarity),
            NormalHeaderVisibility: compact ? Visibility.Collapsed : Visibility.Visible,
            CompactHeaderVisibility: compact ? Visibility.Visible : Visibility.Collapsed,
            OperatorChips: chips,
            AllOperators: tooltipRows);
    }

    // アークナイツ本編のレアリティ配色に合わせた色。GuaranteedMinRarityは「この組み合わせで
    // 保証される最低レアリティ」であり、通知対象(IsRecommended)は常に4以上なので3以下は
    // 実際には出現しないが、念のためフォールバックを用意している。
    private static readonly SolidColorBrush Rarity6Brush = Freeze(0xFF, 0xFF, 0x9A, 0x2E);
    private static readonly SolidColorBrush Rarity5Brush = Freeze(0xFF, 0xFF, 0xD5, 0x4F);
    private static readonly SolidColorBrush Rarity4Brush = Freeze(0xFF, 0xC0, 0x92, 0xFF);
    private static readonly SolidColorBrush FallbackRarityBrush = Freeze(0xFF, 0x9E, 0x9E, 0x9E);

    // オペレーターチップ用の低不透明度版。枠線だとカード全体の枠線と同じ見た目で紛らわしい
    // (ユーザー指摘)ため、チップ側はごく薄い塗りつぶしにして視覚的な階層を分けている。
    private const byte TintAlpha = 0x30;
    private static readonly SolidColorBrush Rarity6TintBrush = Freeze(TintAlpha, 0xFF, 0x9A, 0x2E);
    private static readonly SolidColorBrush Rarity5TintBrush = Freeze(TintAlpha, 0xFF, 0xD5, 0x4F);
    private static readonly SolidColorBrush Rarity4TintBrush = Freeze(TintAlpha, 0xC0, 0x92, 0xFF);
    private static readonly SolidColorBrush FallbackTintBrush = Freeze(TintAlpha, 0x9E, 0x9E, 0x9E);

    private static SolidColorBrush Freeze(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
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

    private static SolidColorBrush TintBrush(int? rarity) => rarity switch
    {
        6 => Rarity6TintBrush,
        5 => Rarity5TintBrush,
        4 => Rarity4TintBrush,
        _ => FallbackTintBrush,
    };

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    /// <summary>
    /// 通知1件分の表示用データ。ドメインモデル(<see cref="CombinationResult"/>)をそのまま
    /// バインドせず、ここで整形済みの文字列・色を持たせることでXAML側のテンプレートを単純に保つ。
    /// NormalHeaderVisibility/CompactHeaderVisibilityは、コンパクト表示設定に応じてどちらか
    /// 一方だけがVisibleになる(XAML側にIF分岐や変換コンバーターを持ち込まないため)。
    /// </summary>
    private sealed record CombinationDisplayItem(
        string TagsText,
        string RarityText,
        string CountText,
        SolidColorBrush RarityBrush,
        Visibility NormalHeaderVisibility,
        Visibility CompactHeaderVisibility,
        IReadOnlyList<OperatorChip> OperatorChips,
        IReadOnlyList<OperatorTooltipRow> AllOperators);

    /// <summary>対象オペレーター1人分の表示(名前+そのオペレーター自身のレアリティ色の薄い塗り)。</summary>
    private sealed record OperatorChip(string Name, SolidColorBrush TintBrush);

    /// <summary>ホバー時のツールチップに列挙する対象オペレーター1人分の行(省略なしの全件)。</summary>
    private sealed record OperatorTooltipRow(string Name, string RarityLabel, SolidColorBrush RarityBrush);
}
