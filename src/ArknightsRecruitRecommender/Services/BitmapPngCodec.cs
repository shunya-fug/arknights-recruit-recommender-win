using System.IO;
using System.Windows.Media.Imaging;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// BitmapSourceをPNGとしてエンコードする処理を1箇所にまとめたもの。OCR用の変換
/// (TagOcrService)とデバッグ用のキャプチャ画像保存(DebugArtifactWriter)の両方で
/// 同じエンコード手順が必要になるため共通化している。
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
