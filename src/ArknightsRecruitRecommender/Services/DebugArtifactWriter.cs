using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// Writes the intermediate output of a single recruitment check (captured frame, raw OCR
/// words, matched tags, combination results) to disk so it can be inspected after the fact -
/// there is no way to watch the live game screen while debugging remotely.
/// </summary>
public static class DebugArtifactWriter
{
    public static string Write(RecruitmentCheckResult result, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var pngPath = Path.Combine(outputDirectory, $"{timestamp}_frame.png");
        SavePng(result.Frame, pngPath);

        var summaryPath = Path.Combine(outputDirectory, $"{timestamp}_result.txt");
        File.WriteAllText(summaryPath, BuildSummary(result), Encoding.UTF8);

        return outputDirectory;
    }

    private static void SavePng(BitmapSource frame, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string BuildSummary(RecruitmentCheckResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== OCRで検出された生の単語 ===");
        foreach (var word in result.RawOcrWords)
        {
            sb.AppendLine($"  \"{word.Text}\" (x={word.X:F0}, y={word.Y:F0}, w={word.Width:F0}, h={word.Height:F0})");
        }

        sb.AppendLine();
        sb.AppendLine("=== 既知タグ一覧とのマッチ結果 ===");
        sb.AppendLine(result.MatchedTags.Count == 0
            ? "  (一致なし)"
            : string.Join(", ", result.MatchedTags));

        sb.AppendLine();
        sb.AppendLine("=== タグ組み合わせの判定結果 ===");
        foreach (var combo in result.Combinations)
        {
            var marker = combo.IsRecommended ? "[おすすめ] " : "";
            sb.AppendLine($"  {marker}[{string.Join(" / ", combo.Tags)}] -> {combo.GuaranteedMinRarity}★以上確定 ({combo.MatchingOperators.Count}件)");
        }

        return sb.ToString();
    }
}
