using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public class TagMatcherTests
{
    private static DetectedTag Word(string text) => new(text, 0, 0, 0, 0);

    [Fact]
    public void ExactMatch_IsMatched()
    {
        var detected = new[] { Word("Guard") };
        var knownTags = new[] { "Guard", "Sniper" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "Guard" }, result);
    }

    [Fact]
    public void NoSimilarText_IsNotMatched()
    {
        var detected = new[] { Word("Xyzzy") };
        var knownTags = new[] { "Guard", "Sniper" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Empty(result);
    }

    [Fact]
    public void CaseAndWhitespaceDifferences_AreNormalizedAway()
    {
        var detected = new[] { Word("  guard  ") };
        var knownTags = new[] { "Guard" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "Guard" }, result);
    }

    [Theory]
    [InlineData("Guar")] // OCRが末尾を読み落とした(部分一致)
    [InlineData("XGuardX")] // OCRが前後に余分な文字を拾った(逆方向の部分一致)
    public void PartialContainment_IsMatched(string ocrText)
    {
        var detected = new[] { Word(ocrText) };
        var knownTags = new[] { "Guard" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "Guard" }, result);
    }

    [Fact]
    public void ShortFragmentContainedInLongerTag_IsNotMatched()
    {
        // 公開求人画面以外の画面でOCRが拾った無関係な短い断片が、長いタグ名の部分文字列に
        // 偶然一致して誤検出を起こさないようにするための回帰テスト。
        var detected = new[] { Word("Gu") };
        var knownTags = new[] { "Guard" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Empty(result);
    }

    [Fact]
    public void WithinEditDistanceTolerance_IsMatched()
    {
        // "Guard" -> "Guaad" は1文字置換(編集距離1)。デフォルトのmaxEditDistance=1で一致するはず。
        var detected = new[] { Word("Guaad") };
        var knownTags = new[] { "Guard" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "Guard" }, result);
    }

    [Fact]
    public void BeyondEditDistanceTolerance_IsNotMatched()
    {
        // "Guard" -> "Gxxrx" は3文字違う(編集距離3)。デフォルトのmaxEditDistance=1では一致しないはず。
        var detected = new[] { Word("Gxxrx") };
        var knownTags = new[] { "Guard" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Empty(result);
    }

    [Fact]
    public void CustomMaxEditDistance_WidensOrNarrowsTolerance()
    {
        var detected = new[] { Word("Guxrx") }; // "Guard"との編集距離は2(a->x, d->x)
        var knownTags = new[] { "Guard" };

        Assert.Empty(TagMatcher.MatchKnownTags(detected, knownTags, maxEditDistance: 1));
        Assert.Equal(new[] { "Guard" }, TagMatcher.MatchKnownTags(detected, knownTags, maxEditDistance: 2));
    }

    [Fact]
    public void MultipleDetectedWords_OnlyRelevantOneCausesMatch()
    {
        var detected = new[] { Word("Sniper"), Word("Guard"), Word("Ranged") };
        var knownTags = new[] { "Guard" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "Guard" }, result);
    }

    [Fact]
    public void ResultOrderFollowsKnownTagsOrderNotDetectionOrder()
    {
        var detected = new[] { Word("Sniper"), Word("Guard") };
        var knownTags = new[] { "Guard", "Sniper" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "Guard", "Sniper" }, result);
    }

    [Fact]
    public void EmptyDetectedList_MatchesNothing()
    {
        var result = TagMatcher.MatchKnownTags(Array.Empty<DetectedTag>(), new[] { "Guard" });

        Assert.Empty(result);
    }

    [Fact]
    public void EmptyKnownTagsList_MatchesNothing()
    {
        var result = TagMatcher.MatchKnownTags(new[] { Word("Guard") }, Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void ExactMatch_DoesNotAlsoFuzzyMatchASimilarSiblingTag()
    {
        // 実機で確認した回帰テスト: 「近距離」と「遠距離」は編集距離1しか離れていないため、
        // OCRが「遠距離」を正確に読み取った場合でも、「近距離」側からの編集距離判定が
        // 独立して成立してしまい、両方が同時に検出されるバグがあった。
        var detected = new[] { Word("遠距離") };
        var knownTags = new[] { "近距離", "遠距離" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "遠距離" }, result);
    }

    [Fact]
    public void JapaneseText_ExactMatchWorks()
    {
        var detected = new[] { Word("前衛タイプ") };
        var knownTags = new[] { "前衛タイプ", "近距離" };

        var result = TagMatcher.MatchKnownTags(detected, knownTags);

        Assert.Equal(new[] { "前衛タイプ" }, result);
    }
}
