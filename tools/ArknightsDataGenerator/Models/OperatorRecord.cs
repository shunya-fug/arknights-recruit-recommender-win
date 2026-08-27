namespace ArknightsDataGenerator.Models;

/// <summary>
/// 出力先(src/ArknightsRecruitRecommender/Data/operators.ja-JP.json)のスキーマ。
/// メインアプリの Models/OperatorInfo.cs と一致させること。
/// </summary>
public sealed class OperatorRecord
{
    public required string Name { get; init; }
    public required int Rarity { get; init; }
    public required List<string> Tags { get; init; }
}
