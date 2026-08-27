using System.Text.Json.Serialization;

namespace ArknightsDataGenerator.Models;

/// <summary>
/// character_table.json の1オペレーター分のエントリ（必要なフィールドのみ）。
/// </summary>
public sealed class CharacterTableEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = ""; // 例: "TIER_1".."TIER_6"

    [JsonPropertyName("profession")]
    public string Profession { get; set; } = "";

    [JsonPropertyName("position")]
    public string Position { get; set; } = "";

    [JsonPropertyName("tagList")]
    public List<string>? TagList { get; set; }
}
