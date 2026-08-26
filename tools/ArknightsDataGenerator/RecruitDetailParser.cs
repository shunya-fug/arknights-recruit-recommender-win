using System.Text.RegularExpressions;

namespace ArknightsDataGenerator;

/// <summary>
/// gacha_table.json の recruitDetail フィールド（ゲーム内「募集可能一覧」表示用テキスト）を
/// レア度ごとのオペレーター名一覧にパースする。
///
/// フォーマット例:
///   ★
///   &lt;@rc.eml&gt;Lancet-2&lt;/&gt; / &lt;@rc.eml&gt;Castle-3&lt;/&gt;
///   --------------------
///   ★★
///   ...
///
/// 注意点: マークアップの閉じタグ "&lt;/&gt;" 自体に "/" が含まれるため、名前の区切り文字である
/// "/" で分割する前に、必ずマークアップタグを除去すること（先に分割すると閉じタグの "/" まで
/// 区切り文字として扱われ、名前が壊れる）。
/// </summary>
public static class RecruitDetailParser
{
    private static readonly Regex TierHeaderPattern = new(@"^★+$", RegexOptions.Compiled);
    private static readonly Regex SeparatorPattern = new(@"^-+$", RegexOptions.Compiled);
    private static readonly Regex MarkupTagPattern = new(@"<@rc\.[a-zA-Z]+>|</>", RegexOptions.Compiled);

    /// <summary>
    /// レア度(1〜6) -> そのレア度に属するオペレーター名一覧、の辞書を返す。
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> ParseTiers(string recruitDetailText)
    {
        var result = new Dictionary<int, List<string>>();
        int? currentTier = null;

        foreach (var rawLine in recruitDetailText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (TierHeaderPattern.IsMatch(line))
            {
                currentTier = line.Length;
                result[currentTier.Value] = new List<string>();
                continue;
            }

            if (currentTier is null || SeparatorPattern.IsMatch(line) || line.Trim().Length == 0)
            {
                continue;
            }

            // マークアップタグを先に除去してから "/" で分割する（順序が逆だと壊れる）。
            var cleaned = MarkupTagPattern.Replace(line, "");
            var names = cleaned
                .Split('/')
                .Select(n => n.Trim())
                .Where(n => n.Length > 0);

            result[currentTier.Value].AddRange(names);
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }
}
