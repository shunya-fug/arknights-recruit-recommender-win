using System.Text.Json;
using ArknightsDataGenerator;
using ArknightsDataGenerator.Models;

const string DefaultGachaSource =
    "https://raw.githubusercontent.com/ArknightsAssets/ArknightsGamedata/master/jp/gamedata/excel/gacha_table.json";
const string DefaultCharacterSource =
    "https://raw.githubusercontent.com/ArknightsAssets/ArknightsGamedata/master/jp/gamedata/excel/character_table.json";

var options = ParseArgs(args);
if (options is null)
{
    return 1;
}

var JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

using var httpClient = new HttpClient();

Console.WriteLine($"gacha_table.json取得元: {options.GachaSource}");
var gachaJson = await ReadSourceAsync(httpClient, options.GachaSource);
var gachaData = JsonSerializer.Deserialize<GachaTableData>(gachaJson, JsonOptions)
    ?? throw new InvalidDataException("gacha_table.jsonの解析に失敗しました。");

Console.WriteLine($"character_table.json取得元: {options.CharacterSource}");
var characterJson = await ReadSourceAsync(httpClient, options.CharacterSource);
var characterDict = JsonSerializer.Deserialize<Dictionary<string, CharacterTableEntry>>(characterJson, JsonOptions)
    ?? throw new InvalidDataException("character_table.jsonの解析に失敗しました。");

var tiers = RecruitDetailParser.ParseTiers(gachaData.RecruitDetail);
var totalNames = tiers.Values.Sum(v => v.Count);
Console.WriteLine($"recruitDetailから{tiers.Count}レア度・{totalNames}名を検出しました。");

var result = OperatorDatasetBuilder.Build(tiers, characterDict.Values.ToList(), gachaData);

if (!result.Success)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"検証エラーが{result.Errors.Count}件見つかったため、出力を書き込まずに終了します。");
    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine($"  - {error}");
    }

    return 1;
}

var outputJson = JsonSerializer.Serialize(result.Operators, new JsonSerializerOptions
{
    WriteIndented = true,
});
await File.WriteAllTextAsync(options.OutputPath, outputJson);

Console.WriteLine($"{result.Operators.Count}件のオペレーターデータを {options.OutputPath} に書き込みました。");
return 0;

static async Task<string> ReadSourceAsync(HttpClient client, string source)
{
    if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return await client.GetStringAsync(source);
    }

    return await File.ReadAllTextAsync(source);
}

static GeneratorOptions? ParseArgs(string[] cliArgs)
{
    string? outputPath = null;
    var gachaSource = DefaultGachaSource;
    var characterSource = DefaultCharacterSource;

    for (var i = 0; i < cliArgs.Length; i++)
    {
        switch (cliArgs[i])
        {
            case "--output" when i + 1 < cliArgs.Length:
                outputPath = cliArgs[++i];
                break;
            case "--gacha-source" when i + 1 < cliArgs.Length:
                gachaSource = cliArgs[++i];
                break;
            case "--character-source" when i + 1 < cliArgs.Length:
                characterSource = cliArgs[++i];
                break;
            default:
                Console.Error.WriteLine($"不明な引数です: {cliArgs[i]}");
                return null;
        }
    }

    if (outputPath is null)
    {
        Console.Error.WriteLine(
            "使い方: ArknightsDataGenerator --output <出力先パス> " +
            "[--gacha-source <URLまたはファイルパス>] [--character-source <URLまたはファイルパス>]");
        return null;
    }

    return new GeneratorOptions(outputPath, gachaSource, characterSource);
}

sealed record GeneratorOptions(string OutputPath, string GachaSource, string CharacterSource);
