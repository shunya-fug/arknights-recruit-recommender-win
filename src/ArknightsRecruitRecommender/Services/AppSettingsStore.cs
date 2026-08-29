using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// AppSettingsを %LOCALAPPDATA%\ArknightsRecruitRecommender\settings.json に
/// 読み書きする。ファイルが無い・壊れている場合はデフォルト設定を返す
/// (設定ファイルの問題でアプリが起動できなくなることを避けるため)。
/// </summary>
public static class AppSettingsStore
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArknightsRecruitRecommender",
        "settings.json");

    // 列挙型(NotificationPosition)を数値ではなく名前で保存し、設定ファイルを目視確認・
    // 手動編集しやすくする。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
