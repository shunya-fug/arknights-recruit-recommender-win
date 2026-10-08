using ArknightsRecruitRecommender.Models;
using ArknightsRecruitRecommender.Services;

namespace ArknightsRecruitRecommender.Tests;

/// <summary>
/// 募集時間の警告(RecruitmentAnalyzer.FindRarityGainInLongerBand)が、実データのランダムな
/// 5タグの組み合わせで、判定結果そのもの(Evaluate)と矛盾しないことを確認する(Issue #34)。
/// 警告の有無と色を同時に決める重要な表示なので、個別のケースのテストだけでなく、不変条件で
/// 広く検証しておく。期待値はFindRarityGainInLongerBandの実装を使わず、Evaluateの結果から
/// 独立に求めている。
/// </summary>
public class BandHintInvariantTests
{
    private const int SampleCount = 200;

    [Fact]
    public void BandHint_IsConsistentWithEvaluate_ForRandomRealTagSets()
    {
        var operators = new OperatorDataProvider("ja-JP").Load();
        var knownTags = OperatorDataProvider.GetAllKnownTags(operators);
        var analyzer = new RecruitmentAnalyzer();
        var random = new Random(20261008);
        var hintCount = 0;

        for (var i = 0; i < SampleCount; i++)
        {
            var tags = knownTags.OrderBy(_ => random.Next()).Take(5).ToList();

            foreach (var band in RecruitTimeBands.All)
            {
                var hint = analyzer.FindRarityGainInLongerBand(tags, operators, band);
                var longerBands = RecruitTimeBands.All.SkipWhile(b => b != band).Skip(1).ToList();
                var current = Displayed(analyzer, tags, operators, band);
                var context = $"band={band}, tags={string.Join("/", tags)}";

                if (hint is null)
                {
                    // 警告なし: どの長い区間でも、おすすめが増えたり確定レアリティが上がったりしない。
                    foreach (var longer in longerBands)
                    {
                        Assert.Empty(Gains(current, Recommended(analyzer, tags, operators, longer)));
                    }

                    continue;
                }

                hintCount++;
                // 警告あり: 案内された区間は選択中より長く、そこで本当に増え、最高レアリティが一致し、
                // それより短い長い区間では増えない(=案内が最短の区間である)。
                Assert.Contains(hint.Band, longerBands);
                foreach (var shorter in longerBands.TakeWhile(b => b != hint.Band))
                {
                    Assert.Empty(Gains(current, Recommended(analyzer, tags, operators, shorter)));
                }

                var gains = Gains(current, Recommended(analyzer, tags, operators, hint.Band));
                Assert.True(gains.Count > 0, context);
                Assert.Equal(gains.Max(), hint.Rarity);
            }
        }

        // 警告ありのケースが1件も無いと、上の検証が空振りになる。
        Assert.True(hintCount > 0, "ランダムな組み合わせに警告ありのケースが含まれていません。");
    }

    [Fact]
    public void BandHint_IsNeverShownInTheLongestBand()
    {
        var operators = new OperatorDataProvider("ja-JP").Load();
        var knownTags = OperatorDataProvider.GetAllKnownTags(operators);
        var analyzer = new RecruitmentAnalyzer();

        // 1タグずつ全種類。長い区間が無いので、常に警告なし。
        foreach (var tag in knownTags)
        {
            Assert.Null(analyzer.FindRarityGainInLongerBand(new[] { tag }, operators, RecruitTimeBand.From740));
        }
    }

    /// <summary>
    /// 選択中の区間で表示される組み合わせ。確定のものは確定レアリティ、狙うレアリティが出る組み合わせ
    /// (確定ではないが表示されている)は、警告の対象にならないようint.MaxValueにする。
    /// </summary>
    private static Dictionary<string, int> Displayed(
        RecruitmentAnalyzer analyzer, IReadOnlyList<string> tags, IReadOnlyList<OperatorInfo> operators, RecruitTimeBand band) =>
        RecruitmentAnalyzer.SelectForDisplay(analyzer.Evaluate(tags, operators, band), band)
            .ToDictionary(r => string.Join("|", r.Tags), r => r.IsRecommended ? r.GuaranteedMinRarity ?? 0 : int.MaxValue);

    private static Dictionary<string, int> Recommended(
        RecruitmentAnalyzer analyzer, IReadOnlyList<string> tags, IReadOnlyList<OperatorInfo> operators, RecruitTimeBand band) =>
        analyzer.Evaluate(tags, operators, band)
            .Where(r => r.IsRecommended)
            .ToDictionary(r => string.Join("|", r.Tags), r => r.GuaranteedMinRarity ?? 0);

    /// <summary>longerの方で、currentに無いか、より高い確定レアリティになっている組み合わせのレアリティ一覧。</summary>
    private static List<int> Gains(Dictionary<string, int> current, Dictionary<string, int> longer) =>
        longer.Where(kv => !current.TryGetValue(kv.Key, out var rarity) || kv.Value > rarity)
              .Select(kv => kv.Value)
              .ToList();

    /// <summary>
    /// エリート・上級エリートは、このアプリが最も重視する高レアのタグ(実データ・ja-JP)。区間ごとの
    /// 判定と警告が、ゲーム仕様どおりになっていることを、ランダムではなく明示的に固定する。
    /// ★5は4:00以上、★6は7:40〜9:00かつ上級エリートタグがあるときだけ候補になる。
    /// </summary>
    [Fact]
    public void EliteTags_FollowTheBandRulesAndHints_WithRealData()
    {
        var operators = new OperatorDataProvider("ja-JP").Load();
        var analyzer = new RecruitmentAnalyzer();

        // 上級エリート(★6): 7:40〜9:00ではおすすめ(★6確定)。それ以外の区間では候補が空でおすすめなし。
        var topLong = analyzer.Evaluate(new[] { "上級エリート" }, operators, RecruitTimeBand.From740);
        Assert.Equal(6, Assert.Single(topLong).GuaranteedMinRarity);
        Assert.Empty(analyzer.Evaluate(new[] { "上級エリート" }, operators, RecruitTimeBand.From400To730));
        Assert.Empty(analyzer.Evaluate(new[] { "上級エリート" }, operators, RecruitTimeBand.UpTo350));

        // ★6は7:40以上でしか出ないので、短い区間では常に7:40以上を案内する。
        Assert.Equal(new BandRarityGain(RecruitTimeBand.From740, 6),
            analyzer.FindRarityGainInLongerBand(new[] { "上級エリート" }, operators, RecruitTimeBand.UpTo350));
        Assert.Equal(new BandRarityGain(RecruitTimeBand.From740, 6),
            analyzer.FindRarityGainInLongerBand(new[] { "上級エリート" }, operators, RecruitTimeBand.From400To730));
        Assert.Null(analyzer.FindRarityGainInLongerBand(new[] { "上級エリート" }, operators, RecruitTimeBand.From740));

        // エリート(★5): 4:00以上でおすすめ(★5確定)。★1〜4の区間では候補が空で、4:00以上を案内する
        // (★5が出始めるのが4:00なので、7:40以上とは案内しない)。
        var eliteMid = analyzer.Evaluate(new[] { "エリート" }, operators, RecruitTimeBand.From400To730);
        Assert.True(Assert.Single(eliteMid).IsRecommended);
        Assert.Empty(analyzer.Evaluate(new[] { "エリート" }, operators, RecruitTimeBand.UpTo350));
        Assert.Equal(new BandRarityGain(RecruitTimeBand.From400To730, 5),
            analyzer.FindRarityGainInLongerBand(new[] { "エリート" }, operators, RecruitTimeBand.UpTo350));
        Assert.Null(analyzer.FindRarityGainInLongerBand(new[] { "エリート" }, operators, RecruitTimeBand.From400To730));
    }
}
