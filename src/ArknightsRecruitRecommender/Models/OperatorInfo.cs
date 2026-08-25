namespace ArknightsRecruitRecommender.Models;

public sealed class OperatorInfo
{
    public required string Name { get; init; }
    public required int Rarity { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
}
