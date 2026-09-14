using System.IO;
using System.Windows.Media.Imaging;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// BitmapSourceをPNGとしてエンコードする処理を1箇所にまとめたもの。現状はデバッグ用の
/// キャプチャ画像保存(DebugArtifactWriter)でのみ使用(OCR用の変換(TagOcrService)は
/// Issue #29でPNG往復を経由しない方式に変更済み)。
/// </summary>
public static class BitmapPngCodec
{
    public static void Encode(BitmapSource source, Stream destination)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(destination);
    }
}
