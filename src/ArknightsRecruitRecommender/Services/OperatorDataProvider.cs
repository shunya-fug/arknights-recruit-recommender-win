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

    /// <summary>
    /// Data/operators.{locale}.json という命名のファイルをDataフォルダから走査し、
    /// 現在選択可能なロケール一覧を返す。新しい言語のデータファイルを追加するだけで
    /// 選択肢に反映されるようにするため、ロケール一覧をハードコードしない。
    /// </summary>
    public static IReadOnlyList<string> GetAvailableLocales()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(dataDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(dataDirectory, "operators.*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)["operators.".Length..])
            .OrderBy(locale => locale, StringComparer.Ordinal)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
