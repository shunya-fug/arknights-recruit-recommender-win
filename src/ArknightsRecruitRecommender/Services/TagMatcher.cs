namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// Matches raw OCR output against the known tag vocabulary. OCR of the in-game font is not
/// always exact (mis-read characters, split/merged words), so this uses exact-match plus
/// edit-distance tolerance rather than requiring a byte-for-byte match. Substring containment
/// is intentionally NOT used as a match condition; see the comment at its removal site.
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
            // normalizedText.Contains(normalizedTag)(検出テキストがタグ名を部分文字列として
            // 含んでいればOKとする方向)は意図的に含めない。実機で、募集タグとは無関係な単語
            // ―「元素耐性」「防御力を400無視」「遠距離術」のような、敵図鑑やオペレーター詳細
            // 画面の説明文―がタグ名を部分文字列として含むだけで誤って一致し、公開求人画面以外
            // での誤通知を引き起こすことを確認した。OCRのクラスタリング(OcrWordClusterer)で
            // ラベル単位の文字列に復元してから照合しているため、前後にノイズが混入するケースは
            // 編集距離側で十分カバーできる。
            var isFuzzyMatch = !isExactMatch && normalizedDetected.Any(normalizedText =>
                !exactlyMatchedTexts.Contains(normalizedText) &&
                LevenshteinDistance(normalizedText, normalizedTag) <= maxEditDistance);

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
