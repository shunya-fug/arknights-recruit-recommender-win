namespace ArknightsRecruitRecommender.Models;

/// <summary>
/// 想定する募集時間の区間。ゲーム仕様上、募集時間によって排出されるレアリティの範囲が
/// 変わる(画面にも「募集可能範囲」として表示される)。判定時に範囲外のレアリティの
/// オペレーターを候補から外し、「9時間なら★1・★2は出ないので実質★4以上確定」のような
/// 組み合わせを検出するために使う(Issue #34)。
/// </summary>
public enum RecruitTimeBand
{
    /// <summary>〜3:50: ★1〜★4。</summary>
    UpTo350,

    /// <summary>4:00〜7:30: ★2〜★5。</summary>
    From400To730,

    /// <summary>7:40〜9:00: ★3〜★5(★6は上級エリートタグが必要)。通常の運用(9時間)。</summary>
    From740,
}

public static class RecruitTimeBands
{
    public static readonly IReadOnlyList<RecruitTimeBand> All =
        [RecruitTimeBand.UpTo350, RecruitTimeBand.From400To730, RecruitTimeBand.From740];

    public static string TimeLabel(this RecruitTimeBand band) => band switch
    {
        RecruitTimeBand.UpTo350 => "1:00 - 3:50",
        RecruitTimeBand.From400To730 => "4:00 - 7:30",
        RecruitTimeBand.From740 => "7:40 - 9:00",
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

    /// <summary>区間の開始時間(「4:00以上にすると」のような案内文用)。</summary>
    public static string StartLabel(this RecruitTimeBand band) => band switch
    {
        RecruitTimeBand.UpTo350 => "1:00",
        RecruitTimeBand.From400To730 => "4:00",
        RecruitTimeBand.From740 => "7:40",
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

    public static string RarityLabel(this RecruitTimeBand band) => band switch
    {
        RecruitTimeBand.UpTo350 => "★１ー４",
        RecruitTimeBand.From400To730 => "★２ー５",
        RecruitTimeBand.From740 => "★３ー５",
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

    /// <summary>
    /// その区間で「狙う」レアリティ。短い区間(〜3:50は★1、4:00〜7:30は★2)は、通常の運用(7:40〜9:00)
    /// では出ない低レアのオペレーター(ロボット等)を狙うときにしか選ばないため、★4以上の確定ではなく、
    /// そのレアリティが出うる組み合わせを表示する。通常の運用区間は★4以上の確定を表示するのでnull。
    /// </summary>
    public static int? TargetRarity(this RecruitTimeBand band) => band switch
    {
        RecruitTimeBand.UpTo350 => 1,
        RecruitTimeBand.From400To730 => 2,
        RecruitTimeBand.From740 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

    /// <summary>
    /// この区間でそのレアリティのオペレーターが排出されうるか。★6は、募集可能範囲(★3〜5)の
    /// 上限を超えて「上級エリート」タグ限定で出るため、通常の運用区間(7:40〜9:00)でのみ
    /// 候補にし、タグが必要かどうかの判定は呼び出し側(RecruitmentAnalyzer)に任せる。
    /// </summary>
    public static bool AllowsRarity(this RecruitTimeBand band, int rarity) => band switch
    {
        RecruitTimeBand.UpTo350 => rarity is >= 1 and <= 4,
        RecruitTimeBand.From400To730 => rarity is >= 2 and <= 5,
        RecruitTimeBand.From740 => rarity is >= 3 and <= 6,
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };
}

/// <summary>
/// 「この区間(以上)にすると、★<see cref="Rarity"/>以上確定の組み合わせが増える」という案内用の情報。
/// </summary>
public sealed record BandRarityGain(RecruitTimeBand Band, int Rarity);
