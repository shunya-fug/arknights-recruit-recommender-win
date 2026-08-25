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
        var matched = new List<string>();

        foreach (var tag in knownTags)
        {
            var normalizedTag = Normalize(tag);
            var isMatch = detected.Any(d =>
            {
                var normalizedText = Normalize(d.Text);
                return normalizedText == normalizedTag
                    || normalizedTag.Contains(normalizedText)
                    || normalizedText.Contains(normalizedTag)
                    || LevenshteinDistance(normalizedText, normalizedTag) <= maxEditDistance;
            });

            if (isMatch)
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
