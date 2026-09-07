using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

public class OcrWordClustererTests
{
    // 実機のOCR結果(手動チェックのdebug-output)を計測した値を元にしたテストデータ。
    // 「狙撃タイプ」は5文字がx=844〜995、y≈791、高さ約34pxの1文字ずつバラバラの単語として
    // 検出され、次のボタン「重装タイプ」との間はx=1144から始まり約115pxの間隔があった。
    private static DetectedTag Word(string text, double x, double y, double width, double height) =>
        new(text, x, y, width, height);

    [Fact]
    public void CharacterFragmentedJapaneseLabel_IsMergedIntoOneRun()
    {
        var words = new[]
        {
            Word("狙", 844, 791, 34, 34),
            Word("撃", 881, 791, 34, 34),
            Word("タ", 920, 791, 30, 33),
            Word("イ", 957, 791, 29, 33),
            Word("プ", 995, 788, 34, 35),
        };

        var runs = OcrWordClusterer.Cluster(words);

        Assert.Equal(new[] { "狙撃タイプ" }, runs.Select(r => r.Text));
    }

    [Fact]
    public void SeparateButtonsOnSameRow_AreNotMerged()
    {
        var words = new[]
        {
            Word("狙", 844, 791, 34, 34),
            Word("撃", 881, 791, 34, 34),
            Word("タ", 920, 791, 30, 33),
            Word("イ", 957, 791, 29, 33),
            Word("プ", 995, 788, 34, 35),
            Word("重", 1144, 791, 34, 33),
            Word("装", 1182, 790, 33, 35),
            Word("タ", 1221, 791, 30, 33),
            Word("イ", 1257, 791, 30, 33),
            Word("プ", 1295, 788, 34, 35),
        };

        var runs = OcrWordClusterer.Cluster(words);

        Assert.Equal(new[] { "狙撃タイプ", "重装タイプ" }, runs.Select(r => r.Text));
    }

    [Fact]
    public void DifferentRows_AreNotMerged()
    {
        var words = new[]
        {
            Word("募", 563, 505, 32, 33),
            Word("集", 599, 505, 32, 33),
            Word("狙", 844, 791, 34, 34),
            Word("撃", 881, 791, 34, 34),
        };

        var runs = OcrWordClusterer.Cluster(words);

        Assert.Equal(new[] { "募集", "狙撃" }, runs.Select(r => r.Text));
    }

    [Fact]
    public void AlreadyWholeWesternWord_PassesThroughUnchanged()
    {
        var words = new[] { Word("Sniper", 100, 100, 80, 20) };

        var runs = OcrWordClusterer.Cluster(words);

        Assert.Equal(new[] { "Sniper" }, runs.Select(r => r.Text));
    }

    [Fact]
    public void EmptyInput_ProducesNoRuns()
    {
        var runs = OcrWordClusterer.Cluster(Array.Empty<DetectedTag>());

        Assert.Empty(runs);
    }

    /// <summary>
    /// 実機の手動チェックのdebug-outputで観測: 「エリート」タグが「工リ」「ー」「ト」の
    /// 3フラグメントに分割され、うち「ー」だけ高さ5px(他の文字は約33〜37px)という極端に
    /// 小さいバウンディングボックスで検出された。行のグルーピングが候補自身の高さを
    /// 許容誤差に使っていたため、「ー」だけ別の行として弾かれ、タグが未検出になっていた
    /// (「工」はエリートの「エ」とよく似た字形のためOCRの1文字誤読で、既知タグとの照合は
    /// 編集距離1のあいまい一致で吸収する想定。ここでは行のグルーピング自体を検証する)。
    /// </summary>
    [Fact]
    public void FragmentWithAbnormallySmallBoundingBox_IsMergedIntoSameRow()
    {
        var words = new[]
        {
            Word("前衛タイプ", 844, 788, 185, 37),
            Word("特殊タイプ", 1145, 788, 184, 37),
            Word("工リ", 1465, 792, 64, 33),
            Word("ト", 1585, 790, 22, 36),
            Word("ー", 1539, 805, 33, 5),
        };

        var runs = OcrWordClusterer.Cluster(words);

        Assert.Equal(new[] { "前衛タイプ", "特殊タイプ", "工リート" }, runs.Select(r => r.Text));
    }

    [Fact]
    public void MergedRun_BoundingBoxIsUnionOfWords()
    {
        var words = new[]
        {
            Word("狙", 844, 791, 34, 34),
            Word("撃", 881, 791, 34, 35),
        };

        var run = Assert.Single(OcrWordClusterer.Cluster(words));

        Assert.Equal(844, run.X);
        Assert.Equal(791, run.Y);
        Assert.Equal(881 + 34 - 844, run.Width);
        Assert.Equal(35, run.Height);
    }
}
