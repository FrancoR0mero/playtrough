using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Catalog;

internal static partial class GameSearchRelevance
{
    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal)
        { "the", "a", "an", "of", "and", "to", "for", "in", "on", "de", "del", "la", "el", "los", "las", "y" };
    private static readonly Dictionary<string, string> RomanNumbers = Enumerable.Range(1, 39)
        .ToDictionary(number => new[] { "", "x", "xx", "xxx" }[number / 10]
            + new[] { "", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix" }[number % 10],
            number => number.ToString(CultureInfo.InvariantCulture));

    public static IReadOnlyList<GameSummary> Rank(string query, IEnumerable<GameSummary> candidates)
    {
        var wanted = Tokens(query);
        if (wanted.Length == 0) return [];
        return candidates.DistinctBy(game => game.ExternalId)
            .Select(game => (Game: game, Tokens: Tokens(game.Name)))
            .Where(item => Matches(wanted, item.Tokens))
            // Keep RAWG's relevance order within each tier, and for a single-word search.
            .OrderBy(item => wanted.Length == 1 ? 0 : Rank(wanted, item.Tokens))
            .Select(item => item.Game).ToArray();
    }

    private static bool Matches(string[] wanted, string[] title)
        => wanted.Select((word, index) => title.Any(candidate => candidate == word
            // Support the last word while typing, but never treat 4 as a prefix of 40.
            || (index == wanted.Length - 1 && word.Length >= 3 && word.All(char.IsLetter)
                && candidate.StartsWith(word, StringComparison.Ordinal)))).All(match => match);

    private static int Rank(string[] wanted, string[] title)
    {
        if (title.SequenceEqual(wanted)) return 0;
        // A franchise prefix such as "Shin Megami Tensei" must not demote the original game.
        if (title.TakeLast(wanted.Length).SequenceEqual(wanted)) return 1;
        if (title.Take(wanted.Length).SequenceEqual(wanted)) return 2;
        return 3;
    }

    private static string[] Tokens(string value)
    {
        var text = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                text.Append(char.ToLowerInvariant(character));
        var words = WordPattern().Matches(text.ToString()).Select(match => match.Value).ToArray();
        // Roman numerals are interpreted only after a name, not an opening word such as "I".
        for (var index = 1; index < words.Length; index++)
            if (RomanNumbers.TryGetValue(words[index], out var number)) words[index] = number;
        var significant = words.Where(word => !Connectors.Contains(word)).ToArray();
        return significant.Length > 0 ? significant : words;
    }

    [GeneratedRegex(@"\p{L}+|\p{Nd}+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
