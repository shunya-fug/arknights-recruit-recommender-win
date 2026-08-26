using System.IO;
using System.Text.Json;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

public sealed class OperatorDataProvider
{
    private readonly string _dataFilePath;

    public OperatorDataProvider(string? dataFilePath = null, string locale = "ja-JP")
    {
        _dataFilePath = dataFilePath
            ?? Path.Combine(AppContext.BaseDirectory, "Data", $"operators.{locale}.json");
    }

    public IReadOnlyList<OperatorInfo> Load()
    {
        var json = File.ReadAllText(_dataFilePath);
        var operators = JsonSerializer.Deserialize<List<OperatorInfo>>(json, JsonOptions)
            ?? throw new InvalidDataException($"Failed to parse operator data from {_dataFilePath}");
        return operators;
    }

    public static IReadOnlyList<string> GetAllKnownTags(IReadOnlyList<OperatorInfo> operators) =>
        operators.SelectMany(o => o.Tags).Distinct().OrderBy(t => t).ToList();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
