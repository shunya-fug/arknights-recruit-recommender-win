using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace ArknightsRecruitRecommender.Services;

public sealed record DetectedTag(string Text, double X, double Y, double Width, double Height);

/// <summary>
/// Windows標準のOCRエンジン(Windows.Media.Ocr)をラップし、外部OCR依存(Tesseract等)を
/// 増やさずに済むようにする。ゲーム内の独特なフォントはOCRで完全一致しないことがあるため、
/// 認識結果と既知タグとの照合(あいまい一致)は呼び出し側(TagMatcher)で行う。
///
/// 言語は常に明示的に指定する(OSのプロファイル言語からの自動選択には頼らない)。ゲームの
/// 表示言語とOSの言語設定が一致するとは限らないため。
/// </summary>
public sealed class TagOcrService
{
    // OCRに渡す直前に画面キャプチャをこの幅へ正規化する(アスペクト比を保ったまま拡大縮小)。
    // 実機検証で判明: Windows.Media.Ocrは特定のタグ(例:「火力」)を、ネイティブ解像度が
    // 高い(=文字が大きく描画される)場合に検出漏れすることがある一方、FHD相当の幅では
    // 安定して検出できた。逆に極端に小さいネイティブ解像度(幅640px程度)からこの幅へ拡大した
    // 場合でも良好な結果が得られることを、実機キャプチャ画像を使った検証で確認済み
    // (幅480px相当まで縮小すると劣化が見え始めるため、実用上のウィンドウサイズなら十分な
    // マージンがある)。この幅の値自体は、実機で問題が再現した2564px幅のキャプチャに対して
    // 0.5〜0.85倍(1282〜2179px)のいずれでも安定して検出できた範囲の中央付近から選んでいる。
    private const int NormalizedWidth = 1920;

    private readonly OcrEngine _engine;

    public TagOcrService(Language language)
    {
        _engine = OcrEngine.TryCreateFromLanguage(language) ?? throw new InvalidOperationException(
            $"OCR言語パック「{language.DisplayName}」({language.LanguageTag})がインストールされていません。" +
            "設定 > 時刻と言語 > 言語と地域 から、対象言語の「文字認識」機能を追加してください。");
    }

    /// <summary>
    /// 指定言語のOCRパックが端末にインストール済みかどうかを、実際にエンジンを作らずに確認する。
    /// 言語選択UIで、選択前に利用可否を示すために使う。
    ///
    /// LanguageTagの完全一致では判定できない。Windows側は言語パックを地域無しの主言語部分
    /// (例:"ja")で登録することがあり、こちらが要求するのは地域付きのタグ(例:"ja-JP")のため、
    /// 主言語部分(ハイフンの前)だけを比較する。
    /// </summary>
    public static bool IsLanguageAvailable(Language language) =>
        OcrEngine.AvailableRecognizerLanguages.Any(l => PrimarySubtag(l.LanguageTag) == PrimarySubtag(language.LanguageTag));

    private static string PrimarySubtag(string languageTag) =>
        languageTag.Split('-')[0].ToLowerInvariant();

    /// <param name="normalizedWidth">
    /// OCRに渡す前に正規化する幅。省略時は<see cref="NormalizedWidth"/>(既定の1920px)。
    /// 特定のタグ(例:「治療」)は1920px幅では構造的に検出できないことが実機検証で判明して
    /// おり(Issue #4)、呼び出し側がその疑いがある場合に別の幅を指定して再試行できるようにする。
    /// </param>
    public async Task<IReadOnlyList<DetectedTag>> RecognizeAsync(BitmapSource capturedFrame, int normalizedWidth = NormalizedWidth)
    {
        var normalized = NormalizeWidth(capturedFrame, normalizedWidth);
        var softwareBitmap = ConvertToSoftwareBitmap(normalized);
        var result = await _engine.RecognizeAsync(softwareBitmap);

        var words = new List<DetectedTag>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                words.Add(new DetectedTag(
                    word.Text,
                    word.BoundingRect.X,
                    word.BoundingRect.Y,
                    word.BoundingRect.Width,
                    word.BoundingRect.Height));
            }
        }

        var clustered = OcrWordClusterer.Cluster(words);

        // 返す座標は、呼び出し側が渡したcapturedFrame(元のキャプチャ画像)の座標系に揃える。
        // 内部で正規化した画像の座標系のまま返すと、手動チェックのdebug-output(座標ログと
        // 実際のフレーム画像を突き合わせて調査する運用)で座標がズレてしまうため。
        var scaleBackToOriginal = (double)capturedFrame.PixelWidth / normalized.PixelWidth;
        return scaleBackToOriginal == 1.0
            ? clustered
            : clustered.Select(tag => tag with
            {
                X = tag.X * scaleBackToOriginal,
                Y = tag.Y * scaleBackToOriginal,
                Width = tag.Width * scaleBackToOriginal,
                Height = tag.Height * scaleBackToOriginal,
            }).ToList();
    }

    /// <summary>
    /// キャプチャ画像を指定の幅へ正規化する(アスペクト比は保持)。既定幅についての詳細は
    /// フィールドのコメント参照。
    /// </summary>
    private static BitmapSource NormalizeWidth(BitmapSource source, int targetWidth)
    {
        // PixelWidthが0(何らかの理由で縦横比が壊れた不正なフレーム)の場合、スケール計算が
        // InfinityになりTransformedBitmapの生成で例外になる。呼び出し元(RecruitmentMonitor
        // Service.TickAsync)は例外を捕捉して1ティック分スキップするだけだが、そもそも
        // 正規化しようがないフレームなので、ここで早期に諦めて元のフレームをそのまま返す方が
        // 意図が明確。
        if (source.PixelWidth <= 0 || source.PixelWidth == targetWidth)
        {
            return source;
        }

        var scale = (double)targetWidth / source.PixelWidth;
        var transformed = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    /// <summary>
    /// WPFのBitmapSourceからWinRTのSoftwareBitmapへ、生のピクセルバイト列を直接コピーして
    /// 変換する。以前はPNGへエンコードしてからWinRT側で再デコードする往復方式だったが、
    /// 実測で圧縮・展開のコストがOCR本体(RecognizeAsync)より支配的だったため
    /// (Issue #29)、圧縮を経由しないこの方式に置き換えた。
    /// </summary>
    private static Windows.Graphics.Imaging.SoftwareBitmap ConvertToSoftwareBitmap(BitmapSource source)
    {
        // WinRT側が要求するBgra8+Premultipliedと一致するPbgra32へ、必要な場合のみ変換する
        // (キャプチャ・正規化後のBitmapSourceが既にPbgra32であるケースの無駄な変換を避ける)。
        var pbgra32Source = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        var width = pbgra32Source.PixelWidth;
        var height = pbgra32Source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        pbgra32Source.CopyPixels(pixels, stride, 0);

        var softwareBitmap = new Windows.Graphics.Imaging.SoftwareBitmap(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            width,
            height,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        softwareBitmap.CopyFromBuffer(pixels.AsBuffer());
        return softwareBitmap;
    }
}
