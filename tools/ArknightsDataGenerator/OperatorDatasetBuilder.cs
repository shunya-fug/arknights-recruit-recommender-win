using ArknightsDataGenerator.Models;

namespace ArknightsDataGenerator;

public sealed class BuildResult
{
    public required List<OperatorRecord> Operators { get; init; }
    public required List<string> Errors { get; init; }

    public bool Success => Errors.Count == 0;
}

/// <summary>
/// recruitDetail からパースしたレア度別名簿と character_table.json / gacha_table.json の
/// 補助データを突き合わせて、最終的な operators.ja-JP.json 相当のレコード一覧を組み立てる。
///
/// 生成ロジックがgame data側の想定外の変化（未知のprofession値、rarityの食い違い等）を
/// 検知できるよう、疑わしい点は例外にせずErrorsに積んで返す。呼び出し側(Program.cs)は
/// Errorsが1件でもあれば出力を書き込まずに失敗終了させること
/// （自動更新パイプラインが壊れたデータを気付かず生成し続けるのを防ぐため）。
/// </summary>
public static class OperatorDatasetBuilder
{
    // gachaTagsのtagIdは、CN/JP/EN等リージョンをまたいで共通の番号体系になっている
    // (例: id=1は各リージョンでそれぞれ「前衛タイプ」「Guard」等、意味は同じ)。実際にJP版と
    // EN版のgachaTagsを突き合わせて確認済み。そのため、タグの表示文字列(言語依存)は
    // コードに決め打ちせず、このtagIdをキーに各リージョンのgachaTagsから動的に引く。
    // ゲームがまだ知らない新しいprofession値に遭遇した場合はBuildResult.Errorsで検知する
    // (黙って無視しない)。
    private static readonly Dictionary<string, int> ProfessionTagId = new()
    {
        ["WARRIOR"] = 1,
        ["SNIPER"] = 2,
        ["TANK"] = 3,
        ["MEDIC"] = 4,
        ["SUPPORT"] = 5,
        ["CASTER"] = 6,
        ["SPECIAL"] = 7,
        ["PIONEER"] = 8,
    };

    private static readonly Dictionary<string, int> PositionTagId = new()
    {
        ["MELEE"] = 9,
        ["RANGED"] = 10,
    };

    // character_table.jsonに紛れ込む、オペレーターではない既知のエンティティのprofession値。
    // 実際に観測されたもの: "TRAP"(オペレーターのスキルで召喚される罠オブジェクト。本体と
    // 同じ表示名を持つことがある)。新たに同種のケースが見つかった場合はここに追加すること。
    private static readonly HashSet<string> NonOperatorProfessions = new() { "TRAP" };

    private static readonly Dictionary<string, int> RarityMap = new()
    {
        ["TIER_1"] = 1,
        ["TIER_2"] = 2,
        ["TIER_3"] = 3,
        ["TIER_4"] = 4,
        ["TIER_5"] = 5,
        ["TIER_6"] = 6,
    };

    public static BuildResult Build(
        IReadOnlyDictionary<int, IReadOnlyList<string>> tiers,
        IReadOnlyList<CharacterTableEntry> characters,
        GachaTableData gachaData)
    {
        var errors = new List<string>();
        var operators = new List<OperatorRecord>();

        // recruitDetailのパース自体が壊れている(★の記号が変わった、区切りが変わった等で
        // ParseTiersが空/一部欠落を返す)場合に、それに気付かず空/欠落したデータで
        // 「成功」を返さないようにする。1〜6のレア度が全て揃っていることを必須とする。
        for (var rarity = 1; rarity <= 6; rarity++)
        {
            if (!tiers.TryGetValue(rarity, out var names) || names.Count == 0)
            {
                errors.Add(
                    $"recruitDetailから★{rarity}のオペレーターが1件も検出できませんでした。" +
                    "recruitDetailのフォーマットが変わった可能性があるため、パース処理を確認してください。");
            }
        }

        if (errors.Count > 0)
        {
            return new BuildResult { Operators = operators, Errors = errors };
        }

        // character_table.jsonには、オペレーター本体だけでなく、そのオペレーターのスキルで
        // 召喚される罠オブジェクトが同じ表示名で紛れ込んでいることが確認されている
        // (実例: マンティコア/ユーネクテス/パッセンジャーの罠オブジェクトが本体と同名、
        // profession:"TRAP")。これらは確認済みのものだけを明示的に除外する
        // (「既知の職業タグに無いものは全部無視」にすると、将来本当に新しい職業が追加された時に
        // ReportsErrorForUnknownProfession相当のエラーで気付けなくなってしまうため)。
        // 除外してもなお同名で複数残る場合は、本当に判別不能なのでエラーとする(先頭要素を採用しない)。
        var charactersByName = characters
            .Where(c => c is not null)
            .Where(c => !NonOperatorProfessions.Contains(c.Profession))
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.ToList());

        // レアリティ(0始まり)ごとの特別求人タグを specialTagRarityTable + gachaTags から解決する。
        // 文字列を決め打ちしない(将来タグ名が変わっても追従できるようにするため)。
        var tagNameById = gachaData.GachaTags.ToDictionary(t => t.TagId, t => t.TagName);
        var specialTagByRarityIndex = new Dictionary<int, string>();
        foreach (var (tagIdStr, rarityIndices) in gachaData.SpecialTagRarityTable)
        {
            if (!int.TryParse(tagIdStr, out var tagId) || !tagNameById.TryGetValue(tagId, out var tagName))
            {
                errors.Add($"specialTagRarityTableのtagId={tagIdStr}に対応するgachaTagsのタグ名が見つかりません。");
                continue;
            }

            foreach (var rarityIndex in rarityIndices)
            {
                specialTagByRarityIndex[rarityIndex] = tagName;
            }
        }

        foreach (var (tier, names) in tiers)
        {
            foreach (var name in names)
            {
                if (!charactersByName.TryGetValue(name, out var candidates))
                {
                    errors.Add($"「{name}」(recruitDetail上は★{tier})がcharacter_table.jsonに見つかりません。");
                    continue;
                }

                if (candidates.Count > 1)
                {
                    errors.Add(
                        $"「{name}」に該当するcharacter_table.jsonのエントリが{candidates.Count}件あり、" +
                        "どれを採用すべきか自動判別できません(手動での確認が必要です)。");
                    continue;
                }

                var entry = candidates[0];

                if (!RarityMap.TryGetValue(entry.Rarity, out var rarity))
                {
                    errors.Add($"「{name}」のrarity値「{entry.Rarity}」が未知の形式です。");
                    continue;
                }

                if (rarity != tier)
                {
                    errors.Add($"「{name}」: recruitDetail上は★{tier}だが、character_table.json上は★{rarity}。");
                    continue;
                }

                if (!TryResolveTagName(ProfessionTagId, tagNameById, entry.Profession, out var professionTag, out var professionError))
                {
                    errors.Add($"「{name}」のprofession値「{entry.Profession}」: {professionError}");
                    continue;
                }

                if (!TryResolveTagName(PositionTagId, tagNameById, entry.Position, out var positionTag, out var positionError))
                {
                    errors.Add($"「{name}」のposition値「{entry.Position}」: {positionError}");
                    continue;
                }

                var tags = new List<string> { professionTag, positionTag };
                tags.AddRange(entry.TagList ?? new List<string>());

                if (specialTagByRarityIndex.TryGetValue(rarity - 1, out var specialTag))
                {
                    tags.Add(specialTag);
                }

                operators.Add(new OperatorRecord { Name = name, Rarity = rarity, Tags = tags });
            }
        }

        var duplicateNames = operators
            .GroupBy(o => o.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var dup in duplicateNames)
        {
            errors.Add($"「{dup}」が複数のレア度枠に重複して登場しています。");
        }

        return new BuildResult
        {
            Operators = operators.OrderBy(o => o.Rarity).ThenBy(o => o.Name, StringComparer.Ordinal).ToList(),
            Errors = errors,
        };
    }

    /// <summary>
    /// enum値(例:"WARRIOR")→tagId→gachaTagsのタグ名、の2段階で解決する。
    /// 失敗時は、コード側の対応表(enumToTagId)自体に無いのか、それとも対応表にはあるが
    /// そのリージョンのgachaTagsにtagIdが存在しないのかを区別してreasonに返す
    /// (前者は本当に未知のenum値、後者はそのリージョンのデータが不完全、と原因が違うため)。
    /// </summary>
    private static bool TryResolveTagName(
        IReadOnlyDictionary<string, int> enumToTagId,
        IReadOnlyDictionary<int, string> tagNameById,
        string enumValue,
        out string tagName,
        out string reason)
    {
        if (!enumToTagId.TryGetValue(enumValue, out var tagId))
        {
            tagName = "";
            reason = $"未知の値です(タグ対応表に追加が必要)。";
            return false;
        }

        if (!tagNameById.TryGetValue(tagId, out var name))
        {
            tagName = "";
            reason = $"tagId={tagId}に対応するタグ名がこのリージョンのgachaTagsに見つかりません。";
            return false;
        }

        tagName = name;
        reason = "";
        return true;
    }
}
