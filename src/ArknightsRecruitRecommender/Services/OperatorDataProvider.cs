using System.IO;
using System.Reflection;
using System.Text.Json;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// オペレーター/タグデータ(Data/operators.{locale}.json)を読み込む。単一exe配布時に
/// Dataフォルダを別途持ち歩く必要が無いよう、csproj側でアセンブリの埋め込みリソース
/// (論理名 "Data.operators.{locale}.json")として組み込んでいるため、ファイルパスではなく
/// アセンブリのリソースストリームから読み込む。
/// </summary>
public sealed class OperatorDataProvider
{
    private const string ResourcePrefix = "Data.operators.";
    private const string ResourceSuffix = ".json";

    private readonly string _locale;

    public OperatorDataProvider(string locale = "ja-JP")
    {
        _locale = locale;
    }

    public IReadOnlyList<OperatorInfo> Load()
    {
        var resourceName = ResourcePrefix + _locale + ResourceSuffix;
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"埋め込みリソース {resourceName} が見つかりません。");

        return JsonSerializer.Deserialize<List<OperatorInfo>>(stream, JsonOptions)
            ?? throw new InvalidDataException($"オペレーターデータ({resourceName})の解析に失敗しました。");
    }

    public static IReadOnlyList<string> GetAllKnownTags(IReadOnlyList<OperatorInfo> operators) =>
        operators.SelectMany(o => o.Tags).Distinct().OrderBy(t => t).ToList();

    /// <summary>
    /// 埋め込み済みのoperators.{locale}.jsonリソース一覧からロケール一覧を返す。
    /// 新しい言語のデータファイルを追加するだけで選択肢に反映されるようにするため、
    /// ロケール一覧をハードコードしない。
    /// </summary>
    public static IReadOnlyList<string> GetAvailableLocales()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..^ResourceSuffix.Length])
            .OrderBy(locale => locale, StringComparer.Ordinal)
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
