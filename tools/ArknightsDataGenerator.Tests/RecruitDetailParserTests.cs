namespace ArknightsDataGenerator.Tests;

public class RecruitDetailParserTests
{
    [Fact]
    public void ParsesMultipleTiersSeparatedByDashes()
    {
        const string text = "★\n" +
                             "<@rc.eml>Lancet-2</> / <@rc.eml>Castle-3</>\n" +
                             "--------------------\n" +
                             "★★\n" +
                             "ヤトウ / ノイルホーン\n";

        var tiers = RecruitDetailParser.ParseTiers(text);

        Assert.Equal(new[] { "Lancet-2", "Castle-3" }, tiers[1]);
        Assert.Equal(new[] { "ヤトウ", "ノイルホーン" }, tiers[2]);
    }

    [Fact]
    public void ClosingMarkupTagSlashDoesNotBreakNameSplitting()
    {
        // "</>" 自体に "/" が含まれるため、マークアップ除去より先に "/" で分割すると
        // "Lancet-2<" ">" のように壊れる。この回帰を防ぐためのテスト。
        const string text = "★\n<@rc.eml>Lancet-2</> / <@rc.eml>Castle-3</>\n";

        var tiers = RecruitDetailParser.ParseTiers(text);

        Assert.Equal(new[] { "Lancet-2", "Castle-3" }, tiers[1]);
    }

    [Fact]
    public void TrimsWhitespaceIncludingFullWidthSpace()
    {
        const string text = "★★★★★\n" +
                             "ツキノギ /　レオンハルト\n"; // 　 = 全角スペース

        var tiers = RecruitDetailParser.ParseTiers(text);

        Assert.Equal(new[] { "ツキノギ", "レオンハルト" }, tiers[5]);
    }

    [Fact]
    public void IgnoresUnmarkedNamesTheSameAsMarkedOnes()
    {
        // 緑文字(<@rc.eml>)は「公開求人限定」の意味付けだが、プール判定自体には無関係
        // (どちらも公開求人プールのメンバー)なので同様に扱う。
        const string text = "★★★\n<@rc.eml>アドナキエル</> / フェン / バニラ\n";

        var tiers = RecruitDetailParser.ParseTiers(text);

        Assert.Equal(new[] { "アドナキエル", "フェン", "バニラ" }, tiers[3]);
    }

    [Fact]
    public void EmptyLinesAndHeaderTextBeforeFirstTierAreIgnored()
    {
        const string text = "<@rc.title>公開求人説明</>\n\n<@rc.em>説明文</>\n\n★\nLancet-2\n";

        var tiers = RecruitDetailParser.ParseTiers(text);

        Assert.Single(tiers);
        Assert.Equal(new[] { "Lancet-2" }, tiers[1]);
    }
}
