using ArknightsDataGenerator.Models;

namespace ArknightsDataGenerator.Tests;

public class OperatorDatasetBuilderTests
{
    private static GachaTableData SampleGachaData() => new()
    {
        // tagId 1〜10は職業/位置タグの解決にも使われるため、テストで使う職業/位置は
        // 実際のtagId体系(OperatorDatasetBuilder.ProfessionTagId/PositionTagId)に
        // 合わせて用意しておく。
        GachaTags = new List<GachaTag>
        {
            new() { TagId = 1, TagName = "前衛タイプ" },
            new() { TagId = 2, TagName = "狙撃タイプ" },
            new() { TagId = 3, TagName = "重装タイプ" },
            new() { TagId = 4, TagName = "医療タイプ" },
            new() { TagId = 7, TagName = "特殊タイプ" },
            new() { TagId = 8, TagName = "先鋒タイプ" },
            new() { TagId = 9, TagName = "近距離" },
            new() { TagId = 10, TagName = "遠距離" },
            new() { TagId = 11, TagName = "上級エリート" },
            new() { TagId = 14, TagName = "エリート" },
        },
        SpecialTagRarityTable = new Dictionary<string, List<int>>
        {
            ["11"] = new() { 5 }, // 0始まりの5 = ★6
            ["14"] = new() { 4 }, // 0始まりの4 = ★5
        },
    };

    /// <summary>
    /// 「1〜6の全レア度が存在すること」という検証を満たすため、テスト対象外のレア度には
    /// ダミーの1件を割り当てた完全な階級表を作る。<paramref name="overrideTier"/>だけは
    /// テストで指定した名前一覧に差し替える。
    /// </summary>
    private static Dictionary<int, IReadOnlyList<string>> TiersWithOverride(int overrideTier, params string[] names)
    {
        var tiers = new Dictionary<int, IReadOnlyList<string>>();
        for (var r = 1; r <= 6; r++)
        {
            tiers[r] = r == overrideTier ? names : new List<string> { $"filler{r}" };
        }

        return tiers;
    }

    /// <summary>
    /// TiersWithOverride が参照するダミーキャラクター(filler1〜filler6)を含む
    /// character_table.jsonのエントリ一覧。テスト固有のエントリを追加で渡せる。
    /// </summary>
    private static List<CharacterTableEntry> CharactersWithFillers(params CharacterTableEntry[] extra)
    {
        var list = new List<CharacterTableEntry>();
        for (var r = 1; r <= 6; r++)
        {
            list.Add(new CharacterTableEntry
            {
                Name = $"filler{r}",
                Rarity = $"TIER_{r}",
                Profession = "SNIPER",
                Position = "RANGED",
                TagList = new List<string>(),
            });
        }

        list.AddRange(extra);
        return list;
    }

    [Fact]
    public void BuildsTagsFromProfessionPositionAndTagList()
    {
        var tiers = TiersWithOverride(1, "Lancet-2");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "Lancet-2", Rarity = "TIER_1", Profession = "MEDIC", Position = "RANGED", TagList = new List<string> { "ロボット", "治療" } });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.True(result.Success, string.Join(", ", result.Errors));
        var op = result.Operators.Single(o => o.Name == "Lancet-2");
        Assert.Equal(1, op.Rarity);
        Assert.Equal(new[] { "医療タイプ", "遠距離", "ロボット", "治療" }, op.Tags);
    }

    [Fact]
    public void ProfessionAndPositionTagNamesComeFromGachaTagsNotHardcodedJapanese()
    {
        // 職業/位置タグの文字列は言語ごとのgachaTagsから動的に引く設計であることの確認。
        // 日本語決め打ちなら、このテスト(gachaTagsが英語)では日本語のタグ名が返ってきてしまう。
        var tiers = TiersWithOverride(1, "Lancet-2");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "Lancet-2", Rarity = "TIER_1", Profession = "MEDIC", Position = "RANGED", TagList = new List<string> { "Robot", "Healing" } });

        var englishGachaData = new GachaTableData
        {
            GachaTags = new List<GachaTag>
            {
                new() { TagId = 2, TagName = "Sniper" }, // fillerの職業(SNIPER)解決用
                new() { TagId = 4, TagName = "Medic" },
                new() { TagId = 9, TagName = "Melee" },
                new() { TagId = 10, TagName = "Ranged" },
            },
            SpecialTagRarityTable = new Dictionary<string, List<int>>(),
        };

        var result = OperatorDatasetBuilder.Build(tiers, characters, englishGachaData);

        Assert.True(result.Success, string.Join(", ", result.Errors));
        var op = result.Operators.Single(o => o.Name == "Lancet-2");
        Assert.Equal(new[] { "Medic", "Ranged", "Robot", "Healing" }, op.Tags);
    }

    [Fact]
    public void ReportsErrorWhenGachaTagsIsMissingTheProfessionsTagId()
    {
        var tiers = TiersWithOverride(3, "タグ欠損キャラ");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "タグ欠損キャラ", Rarity = "TIER_3", Profession = "TANK", Position = "MELEE", TagList = new List<string>() });

        // TANKのtagId=3がgachaTagsに存在しないケース(そのリージョンのデータが不完全な場合を想定)。
        var incompleteGachaData = new GachaTableData
        {
            GachaTags = new List<GachaTag>
            {
                new() { TagId = 2, TagName = "狙撃タイプ" }, // fillerの職業(SNIPER)解決用
                new() { TagId = 9, TagName = "近距離" },
                new() { TagId = 10, TagName = "遠距離" },
            },
            SpecialTagRarityTable = new Dictionary<string, List<int>>(),
        };

        var result = OperatorDatasetBuilder.Build(tiers, characters, incompleteGachaData);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("タグ欠損キャラ"));
    }

    [Fact]
    public void AppliesTopOperatorTagForRarity6()
    {
        var tiers = TiersWithOverride(6, "シルバーアッシュ");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "シルバーアッシュ", Rarity = "TIER_6", Profession = "WARRIOR", Position = "MELEE", TagList = new List<string> { "火力" } });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.True(result.Success, string.Join(", ", result.Errors));
        Assert.Contains("上級エリート", result.Operators.Single(o => o.Name == "シルバーアッシュ").Tags);
    }

    [Fact]
    public void AppliesEliteTagForRarity5ButNotRarity4()
    {
        var tiers = new Dictionary<int, IReadOnlyList<string>>();
        for (var r = 1; r <= 6; r++)
        {
            tiers[r] = new List<string> { $"filler{r}" };
        }
        tiers[5] = new List<string> { "五星さん" };
        tiers[4] = new List<string> { "四星さん" };

        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "五星さん", Rarity = "TIER_5", Profession = "SNIPER", Position = "RANGED", TagList = new List<string>() },
            new CharacterTableEntry { Name = "四星さん", Rarity = "TIER_4", Profession = "SNIPER", Position = "RANGED", TagList = new List<string>() });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.True(result.Success, string.Join(", ", result.Errors));
        Assert.Contains("エリート", result.Operators.Single(o => o.Name == "五星さん").Tags);
        Assert.DoesNotContain("エリート", result.Operators.Single(o => o.Name == "四星さん").Tags);
    }

    [Fact]
    public void ReportsErrorWhenNameNotFoundInCharacterTable()
    {
        var tiers = TiersWithOverride(3, "存在しないキャラ");

        var result = OperatorDatasetBuilder.Build(tiers, CharactersWithFillers(), SampleGachaData());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("存在しないキャラ"));
    }

    [Fact]
    public void ReportsErrorWhenRarityDoesNotMatchRecruitDetailTier()
    {
        // recruitDetailでは★3だが、character_table.json上は★4になっている(食い違い)ケース。
        var tiers = TiersWithOverride(3, "ズレてるキャラ");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "ズレてるキャラ", Rarity = "TIER_4", Profession = "TANK", Position = "MELEE", TagList = new List<string>() });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("ズレてるキャラ"));
    }

    [Fact]
    public void ReportsErrorForUnknownProfession()
    {
        var tiers = TiersWithOverride(3, "新職業キャラ");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "新職業キャラ", Rarity = "TIER_3", Profession = "UNKNOWN_CLASS", Position = "MELEE", TagList = new List<string>() });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("UNKNOWN_CLASS"));
    }

    [Fact]
    public void ReportsErrorWhenAnyRarityTierIsMissingFromRecruitDetail()
    {
        // ★3を丸ごと欠落させる。recruitDetailのフォーマット変化によるパース失敗を模したケース。
        var tiers = new Dictionary<int, IReadOnlyList<string>>();
        for (var r = 1; r <= 6; r++)
        {
            if (r == 3) continue;
            tiers[r] = new List<string> { $"filler{r}" };
        }

        var result = OperatorDatasetBuilder.Build(tiers, CharactersWithFillers(), SampleGachaData());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("★3"));
        // フォーマット崩壊時に空/欠落データを「成功」として書き出さないことが本題のため、
        // 1件もOperatorsに含まれていないことも確認する。
        Assert.Empty(result.Operators);
    }

    [Fact]
    public void MatchesCharacterTableEntryWhoseNameIsWrappedInQuotes()
    {
        // 実際に観測されたケース(EN版): character_table.json上は "'Justice Knight'" のように
        // 前後を単引用符で囲われているが、recruitDetail上は引用符無しの "Justice Knight"。
        var tiers = TiersWithOverride(1, "Justice Knight");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "'Justice Knight'", Rarity = "TIER_1", Profession = "SNIPER", Position = "RANGED", TagList = new List<string> { "Robot", "Support" } });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.True(result.Success, string.Join(", ", result.Errors));
        var op = result.Operators.Single(o => o.Name == "Justice Knight");
        Assert.Equal(1, op.Rarity);
    }

    [Fact]
    public void IgnoresTrapEntriesThatShareAnOperatorsDisplayName()
    {
        // 実際に観測されたケース: オペレーター本体と、そのスキルで召喚される罠オブジェクトが
        // 同じ表示名(例:マンティコア)を持つ。profession:"TRAP"側は無視して本体だけを採用する。
        var tiers = TiersWithOverride(5, "マンティコア");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "マンティコア", Rarity = "TIER_5", Profession = "SPECIAL", Position = "MELEE", TagList = new List<string> { "弱化" } },
            new CharacterTableEntry { Name = "マンティコア", Rarity = "TIER_1", Profession = "TRAP", Position = "MELEE", TagList = new List<string>() });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.True(result.Success, string.Join(", ", result.Errors));
        var op = result.Operators.Single(o => o.Name == "マンティコア");
        Assert.Equal(5, op.Rarity);
    }

    [Fact]
    public void ReportsErrorWhenMultipleCharacterEntriesShareTheSameName()
    {
        var tiers = TiersWithOverride(3, "同名キャラ");
        var characters = CharactersWithFillers(
            new CharacterTableEntry { Name = "同名キャラ", Rarity = "TIER_3", Profession = "TANK", Position = "MELEE", TagList = new List<string>() },
            new CharacterTableEntry { Name = "同名キャラ", Rarity = "TIER_3", Profession = "SNIPER", Position = "RANGED", TagList = new List<string>() });

        var result = OperatorDatasetBuilder.Build(tiers, characters, SampleGachaData());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("同名キャラ") && e.Contains("2件"));
    }
}
