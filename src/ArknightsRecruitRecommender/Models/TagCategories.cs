namespace ArknightsRecruitRecommender.Models;

/// <summary>タグ選択UIでまとめて表示するタグの分類1つ分。Nameが空の場合は見出しを出さない。</summary>
public sealed record TagCategoryGroup(string Name, IReadOnlyList<string> Tags);

/// <summary>
/// 29種類ある公開求人タグを、ゲーム内の分類(資質・配置・職業・特性)ごとにまとめる。
/// タグ選択UI(TagEditWindow・開発補助ツール)で、全タグを1列に並べるより探しやすくするため。
///
/// operators.{locale}.jsonにはタグの分類情報が無いため、分類はここに決め打ちで持つ。実機で
/// 確認済みのja-JPのタグ名のみ対応しており、他ロケールのタグ名は分類に一致しないため、
/// 全て見出し無しの1グループとして従来通り1列で表示する(README「Global版未検証」と同様の
/// 既知の制限)。
/// </summary>
public static class TagCategories
{
    private static readonly (string Name, string[] Tags)[] Definitions =
    [
        ("資質", ["初期", "エリート", "上級エリート"]),
        ("配置", ["近距離", "遠距離"]),
        ("職業", ["先鋒タイプ", "前衛タイプ", "狙撃タイプ", "重装タイプ", "医療タイプ", "補助タイプ", "術師タイプ", "特殊タイプ"]),
        ("特性", ["火力", "治療", "防御", "生存", "範囲攻撃", "支援", "減速", "COST回復", "牽制", "弱化", "ロボット", "高速再配置", "強制移動", "爆発力", "召喚", "元素"]),
    ];

    /// <summary>
    /// 既知タグを分類ごとにまとめて返す。定義済みの分類に含まれないタグは末尾の「その他」に、
    /// 1つも分類に一致しない場合(未対応ロケール)は見出し無しの単一グループにする。
    /// </summary>
    public static IReadOnlyList<TagCategoryGroup> Group(IReadOnlyList<string> knownTags)
    {
        var known = new HashSet<string>(knownTags);
        var groups = new List<TagCategoryGroup>();

        foreach (var (name, tags) in Definitions)
        {
            var present = tags.Where(known.Contains).ToList();
            if (present.Count > 0)
            {
                groups.Add(new TagCategoryGroup(name, present));
            }
        }

        if (groups.Count == 0)
        {
            return [new TagCategoryGroup(string.Empty, knownTags)];
        }

        var categorized = new HashSet<string>(groups.SelectMany(g => g.Tags));
        var others = knownTags.Where(t => !categorized.Contains(t)).ToList();
        if (others.Count > 0)
        {
            groups.Add(new TagCategoryGroup("その他", others));
        }

        return groups;
    }
}
