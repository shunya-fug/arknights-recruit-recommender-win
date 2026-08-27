namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// Matches raw OCR output against the known tag vocabulary. OCR of the in-game font is not
/// always exact (mis-read characters, split/merged words), so this uses simple normalized
/// containment plus edit-distance tolerance rather than requiring an exact string match.
/// </summary>
public static class TagMatcher
{
    public static IReadOnlyList<string> MatchKnownTags(
        IReadOnlyList<DetectedTag> detected,
        IReadOnlyList<string> knownTags,
        int maxEditDistance = 1)
    {
        var normalizedDetected = detected.Select(d => Normalize(d.Text)).ToList();

        // 完全一致した検出テキストは、それ以外のタグへのあいまい一致の根拠から除外する。
        // 「近距離」と「遠距離」のように既知タグ同士が編集距離1しか離れていない組が実在し、
        // OCRが「遠距離」を正しく読み取っていても、タグ単位に独立して判定すると「近距離」
        // からも編集距離1で一致してしまい、両方が同時に検出されるバグを実機で確認した
        // (デバッグ出力: OCR生データには「遠距離」しかないのに一致結果に「近距離」も含まれた)。
        // 完全一致は常にあいまい一致より確度が高いため、その検出テキストは他タグの判定材料としない。
        var exactlyMatchedTexts = new HashSet<string>(
            knownTags.Select(Normalize).Where(normalizedDetected.Contains));

        var matched = new List<string>();

        foreach (var tag in knownTags)
        {
            var normalizedTag = Normalize(tag);
            var isExactMatch = normalizedDetected.Contains(normalizedTag);
            var isFuzzyMatch = !isExactMatch && normalizedDetected.Any(normalizedText =>
                !exactlyMatchedTexts.Contains(normalizedText) &&
                (normalizedText.Contains(normalizedTag)
                    || LevenshteinDistance(normalizedText, normalizedTag) <= maxEditDistance));

            if (isExactMatch || isFuzzyMatch)
            {
                matched.Add(tag);
            }
        }

        return matched;
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();

    private static int LevenshteinDistance(string a, string b)
    {
        var lengthA = a.Length;
        var lengthB = b.Length;
        var distances = new int[lengthA + 1, lengthB + 1];

        for (var i = 0; i <= lengthA; distances[i, 0] = i++) { }
        for (var j = 0; j <= lengthB; distances[0, j] = j++) { }

        for (var i = 1; i <= lengthA; i++)
        {
            for (var j = 1; j <= lengthB; j++)
            {
                var cost = b[j - 1] == a[i - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[lengthA, lengthB];
    }
}
