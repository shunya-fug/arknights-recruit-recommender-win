using System.Text.Json.Serialization;

namespace ArknightsDataGenerator.Models;

/// <summary>
/// gacha_table.json のうち、公開求人プールの判定に必要なフィールドのみ。
/// </summary>
public sealed class GachaTableData
{
    /// <summary>
    /// ゲーム内「募集可能一覧」表示に使われる、レア度ごとに区切られたテキスト。
    /// 公開求人プールの一次情報源（唯一、機械的に正確な情報源）。
    /// </summary>
    [JsonPropertyName("recruitDetail")]
    public string RecruitDetail { get; set; } = "";

    [JsonPropertyName("gachaTags")]
    public List<GachaTag> GachaTags { get; set; } = new();

    /// <summary>
    /// tagId(文字列) -> このタグが確定するレアリティ(0始まり。0=★1 .. 5=★6)の配列。
    /// 例: {"11": [5]} は tagId=11 のタグが★6を確定させることを意味する。
    /// </summary>
    [JsonPropertyName("specialTagRarityTable")]
    public Dictionary<string, List<int>> SpecialTagRarityTable { get; set; } = new();
}

public sealed class GachaTag
{
    [JsonPropertyName("tagId")]
    public int TagId { get; set; }

    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = "";
}
