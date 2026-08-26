using System.IO;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WicBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

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

    public async Task<IReadOnlyList<DetectedTag>> RecognizeAsync(BitmapSource capturedFrame)
    {
        var softwareBitmap = await ConvertToSoftwareBitmapAsync(capturedFrame);
        var result = await _engine.RecognizeAsync(softwareBitmap);

        var detected = new List<DetectedTag>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                detected.Add(new DetectedTag(
                    word.Text,
                    word.BoundingRect.X,
                    word.BoundingRect.Y,
                    word.BoundingRect.Width,
                    word.BoundingRect.Height));
            }
        }

        return detected;
    }

    private static async Task<Windows.Graphics.Imaging.SoftwareBitmap> ConvertToSoftwareBitmapAsync(BitmapSource source)
    {
        using var stream = new MemoryStream();
        BitmapPngCodec.Encode(source, stream);
        stream.Position = 0;

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(stream.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        var decoder = await WicBitmapDecoder.CreateAsync(randomAccessStream);
        return await decoder.GetSoftwareBitmapAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
    }
}
