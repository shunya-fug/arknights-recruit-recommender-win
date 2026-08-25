namespace ArknightsRecruitRecommender.Models;

public sealed class CombinationResult
{
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// The lowest rarity among operators that can appear from this exact tag combination.
    /// Null when the combination matches no operator (should not normally happen for valid tag sets).
    /// </summary>
    public int? GuaranteedMinRarity { get; init; }

    public required IReadOnlyList<OperatorInfo> MatchingOperators { get; init; }

    public bool IsRecommended => GuaranteedMinRarity is >= 4;
}
