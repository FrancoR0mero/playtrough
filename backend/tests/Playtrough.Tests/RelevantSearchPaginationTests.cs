using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Playtrough.Api.Catalog;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class RelevantSearchPaginationTests
{
    private static HttpResponseMessage Page(string? next, params (int Id, string Name)[] items)
        => RawgTestContext.Json(JsonSerializer.Serialize(new {
            next, results = items.Select(item => new { id = item.Id, name = item.Name, slug = $"game-{item.Id}" }),
        }));

    [Fact]
    public async Task FillsLogicalPagesFromRelevantGames_WithoutEmptyPagesOrDuplicates()
    {
        using var context = new RawgTestContext((request, _, _) => {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("40", query["page_size"]);
            Assert.Equal("true", query["search_precise"]);
            Assert.False(query.ContainsKey("search_exact"));
            return Task.FromResult(query["page"].ToString() switch {
                "1" => Page("https://untrusted.example/never-follow", (1, "SWAT 4"), (2, "Persona 4 Arena")),
                "2" => Page("next", (3, "Comanche 4"), (4, "Persona 5")),
                "3" => Page(null, (2, "Persona 4 Arena"), (5, "Shin Megami Tensei: Persona 4"), (6, "Persona 4 Revival")),
                _ => throw new InvalidOperationException("Unexpected upstream page"),
            });
        });
        var first = await context.Provider.SearchAsync("persona 4", 1, 2);
        Assert.Equal([5, 2], first.Items.Select(game => game.ExternalId));
        Assert.True(first.HasMore);
        Assert.False(first.IsLimited);
        var second = await context.Provider.SearchAsync("persona 4", 2, 2);
        Assert.Equal([6], second.Items.Select(game => game.ExternalId));
        Assert.False(second.HasMore);
        Assert.Equal(2, second.Page);
        var beyond = await context.Provider.SearchAsync("persona 4", int.MaxValue, 2);
        Assert.Empty(beyond.Items);
        Assert.False(beyond.HasMore);
        Assert.Equal(3, context.Handler.Count);
        Assert.All(context.Handler.Requests, uri => Assert.Equal("api.rawg.io", uri.Host));
    }

    [Fact]
    public async Task CapsCandidateRequests_AndSignalsLimitedCoverageSeparatelyFromLocalHasMore()
    {
        using var context = new RawgTestContext((_, index, _) => Task.FromResult(Page("next", (index, "Persona 4 Arena"))));
        var result = await context.Provider.SearchAsync("persona 4", 1, 20);
        Assert.Equal(RawgGameCatalogProvider.MaxCandidatePages, context.Handler.Count);
        Assert.Equal(5, result.Items.Count);
        Assert.True(result.IsLimited);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task CapWithNoMatches_ReturnsAnHonestEmptyLimitedResult()
    {
        using var context = new RawgTestContext((_, index, _) => Task.FromResult(Page("next", (index, "SWAT 4"))));
        var result = await context.Provider.SearchAsync("persona 4", 1, 20);
        Assert.Empty(result.Items);
        Assert.True(result.IsLimited);
        Assert.False(result.HasMore);
        Assert.Equal(5, context.Handler.Count);
    }

    [Fact]
    public async Task FailureOnLaterSourcePage_DoesNotCacheAnIncompleteSearch()
    {
        using var context = new RawgTestContext((_, attempt, _) => Task.FromResult(attempt switch {
            1 or 3 => Page("next", (1, "Persona 4 Arena")),
            2 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => Page(null, (2, "Persona 4")),
        }));
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("persona 4", 1, 20));
        var result = await context.Provider.SearchAsync("persona 4", 1, 20);
        Assert.Equal([2, 1], result.Items.Select(game => game.ExternalId));
        Assert.Equal(4, context.Handler.Count);
    }

    [Fact]
    public async Task ConcurrentLogicalPages_ShareTheSameUpstreamSnapshot()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = new RawgTestContext(async (_, _, _) => {
            started.TrySetResult();
            await release.Task;
            return Page(null, (1, "Persona 4"), (2, "Persona 4 Arena"));
        });
        var first = context.Provider.SearchAsync("persona 4", 1, 1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = context.Provider.SearchAsync("persona 4", 2, 1);
        release.SetResult();
        Assert.Equal(1, Assert.Single((await first).Items).ExternalId);
        Assert.Equal(2, Assert.Single((await second).Items).ExternalId);
        Assert.Equal(1, context.Handler.Count);
    }

    [Fact]
    public async Task FakeProvider_UsesTheSameInstallmentMatching()
    {
        var result = await new FakeGameCatalogProvider().SearchAsync("witcher III", 1, 20);
        Assert.Equal("The Witcher 3", Assert.Single(result.Items).Name);
        Assert.False(result.HasMore);
        Assert.False(result.IsLimited);
    }
}
