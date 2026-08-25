using System.IO;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WicBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using WpfBitmapFrame = System.Windows.Media.Imaging.BitmapFrame;

namespace ArknightsRecruitRecommender.Services;

public sealed record DetectedTag(string Text, double X, double Y, double Width, double Height);

/// <summary>
/// Wraps the built-in Windows OCR engine (Windows.Media.Ocr) so no external OCR dependency
/// needs to be bundled. Recognized text is fuzzy-matched against the known tag vocabulary by
/// the caller, since OCR of a stylized in-game font will not always be pixel-perfect.
/// </summary>
public sealed class TagOcrService
{
    private readonly OcrEngine _engine;

    public TagOcrService(Language? language = null)
    {
        _engine = language is not null
            ? OcrEngine.TryCreateFromLanguage(language)
            : OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "No OCR language pack is installed. Install the Windows OCR language pack " +
                "for the language the game UI is displayed in (Settings > Time & Language > Language & region).");
    }

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
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(WpfBitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
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
