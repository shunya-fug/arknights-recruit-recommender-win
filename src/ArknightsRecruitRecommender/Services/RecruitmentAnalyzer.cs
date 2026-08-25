using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Services;

/// <summary>
/// Evaluates the recruitment tag guarantee logic: any subset of 1-3 selected tags whose
/// matching operator pool has a minimum rarity of 4 stars or higher is a "good" combination.
/// This mirrors the game's own rule (subsets larger than 3 tags are never evaluated), so no
/// special-casing for robots / senior operators is needed as long as the operator data's tag
/// lists are accurate.
/// </summary>
public sealed class RecruitmentAnalyzer
{
    public IReadOnlyList<CombinationResult> Evaluate(IReadOnlyList<string> visibleTags, IReadOnlyList<OperatorInfo> operators)
    {
        var results = new List<CombinationResult>();

        foreach (var subset in GetSubsets(visibleTags, maxSize: 3))
        {
            var matches = operators
                .Where(op => subset.All(tag => op.Tags.Contains(tag)))
                .ToList();

            if (matches.Count == 0)
            {
                continue;
            }

            results.Add(new CombinationResult
            {
                Tags = subset,
                GuaranteedMinRarity = matches.Min(op => op.Rarity),
                MatchingOperators = matches,
            });
        }

        return results
            .OrderByDescending(r => r.GuaranteedMinRarity)
            .ThenByDescending(r => r.Tags.Count)
            .ToList();
    }

    private static IEnumerable<IReadOnlyList<string>> GetSubsets(IReadOnlyList<string> tags, int maxSize)
    {
        var count = tags.Count;
        for (var mask = 1; mask < (1 << count); mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) > maxSize)
            {
                continue;
            }

            var subset = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    subset.Add(tags[i]);
                }
            }

            yield return subset;
        }
    }
}
