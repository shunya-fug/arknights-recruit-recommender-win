namespace ArknightsRecruitRecommender.Models;

public sealed class CombinationResult
{
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// このタグの組み合わせで出うるオペレーターのうち、最も低いレアリティ(=確定する最低レアリティ)。
    /// 該当するオペレーターがいない場合はnull(正しいタグの組み合わせでは通常起きない)。
    /// </summary>
    public int? GuaranteedMinRarity { get; init; }

    public required IReadOnlyList<OperatorInfo> MatchingOperators { get; init; }

    public bool IsRecommended => GuaranteedMinRarity is >= 4;
}
