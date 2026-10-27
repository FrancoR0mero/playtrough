using Playtrough.Api.Catalog;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class GameSearchRelevanceTests
{
    [Theory]
    [InlineData("Persona 4", "Persona 4", true)]
    [InlineData("Persona 4", "Shin Megami Tensei: Persona 4", true)]
    [InlineData("Persona 4", "Persona 4 Arena", true)]
    [InlineData("Persona 4", "Persona 4 Golden", true)]
    [InlineData("Persona 4", "Persona 4 Revival", true)]
    [InlineData("Persona 4", "Persona 5", false)]
    [InlineData("Persona 4", "Comanche 4", false)]
    [InlineData("Persona 4", "SWAT 4", false)]
    [InlineData("Persona 4", "4 Elements", false)]
    [InlineData("Persona 4", "Persona 40", false)]
    [InlineData("Persona 4", "Persona 2024", false)]
    [InlineData("Final Fantasy VII", "Final Fantasy 7 Remake", true)]
    [InlineData("Final Fantasy 7", "Final Fantasy VII (1997)", true)]
    [InlineData("Final Fantasy VII", "Final Fantasy VIII", false)]
    [InlineData("Resident Evil IV", "Resident Evil 4 (2005)", true)]
    [InlineData("  PÉRSONA—4  ", "Persona4 Arena", true)]
    [InlineData("The Witcher 3", "Witcher III: Wild Hunt", true)]
    [InlineData("Persona 4 aren", "Persona 4 Arena", true)]
    [InlineData("Persona 4 aren", "Persona 4", false)]
    [InlineData("arena persona 4", "Persona 4 Arena", true)]
    [InlineData("I Am Alive", "I Am Alive", true)]
    [InlineData("I Am Alive", "1 Am Alive", false)]
    [InlineData("XCOM 2", "XCOM 2", true)]
    [InlineData("---", "Persona 4", false)]
    public void MatchesNameAndInstallment_AllowingSubtitlesAndNormalizedSpelling(string query, string title, bool expected)
    {
        var result = GameSearchRelevance.Rank(query, [Game(1, title)]);
        Assert.Equal(expected, result.Count == 1);
    }

    [Fact]
    public void RanksOriginalAheadOfSubtitles_Deduplicates_AndPreservesProviderOrderWithinTiers()
    {
        var result = GameSearchRelevance.Rank("Persona 4", [
            Game(3, "Persona 4 Arena"), Game(5, "SWAT 4"), Game(4, "Persona 4 Revival"),
            Game(2, "Shin Megami Tensei: Persona 4"), Game(1, "Persona 4"), Game(3, "Persona 4 Arena"),
        ]);
        Assert.Equal([1, 2, 3, 4], result.Select(game => game.ExternalId));
    }

    [Fact]
    public void SingleWordSearch_PreservesProviderRelevanceOrder()
    {
        var result = GameSearchRelevance.Rank("persona", [Game(1, "Persona 5"), Game(2, "Persona")]);
        Assert.Equal([1, 2], result.Select(game => game.ExternalId));
    }

    private static GameSummary Game(int id, string name) => new(id, name, $"game-{id}", null, null, []);
}
