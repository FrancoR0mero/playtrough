using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class GamesEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_ReportsLimitedCoverageSeparatelyFromHasMore(bool limited)
    {
        using var context = new GamesApiTestContext();
        context.Provider.SearchResult = new GameSearchResult([], 1, 20, false, limited);
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/games/search?q=persona");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(limited, json.RootElement.GetProperty("isLimited").GetBoolean());
        Assert.False(json.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Theory]
    [InlineData("/api/games/search?q=witcher")]
    [InlineData("/api/games/19")]
    public async Task AnonymousRequests_Return401WithoutProviderOrPersistence(string path)
    {
        using var context = new GamesApiTestContext();
        using var client = context.Factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal(0, context.Provider.SearchCalls + context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task AnyRole_CanSearchAndOpenDetails_WithRealLoginAndJwt(string role)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync(role);
        using var search = await client.GetAsync("/api/games/search?q=witcher");
        using var detail = await client.GetAsync("/api/games/19");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20%20")]
    [InlineData("?q=%20a%20")]
    [InlineData("?q=witcher&page=0")]
    [InlineData("?q=witcher&page=-1")]
    [InlineData("?q=witcher&page=abc")]
    [InlineData("?q=witcher&page=2147483648")]
    [InlineData("?q=witcher&pageSize=0")]
    [InlineData("?q=witcher&pageSize=41")]
    [InlineData("?q=witcher&pageSize=abc")]
    public async Task InvalidSearch_Returns400InvalidQuery_WithoutCallingProvider(string query)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/games/search" + query);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_query");
        Assert.Equal(0, context.Provider.SearchCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Fact]
    public async Task Search_ValidatesLengthAfterTrimming_AndForwardsDefaults()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var longQuery = await client.GetAsync("/api/games/search?q=" + new string('a', 101));
        await AssertProblemAsync(longQuery, HttpStatusCode.BadRequest, "invalid_query");
        var boundary = "  " + new string('a', 100) + "  ";
        using var valid = await client.GetAsync("/api/games/search?q=" + Uri.EscapeDataString(boundary));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal((new string('a', 100), 1, 20), context.Provider.LastSearch!.Value);
        using var twoCharacters = await client.GetAsync("/api/games/search?q=%20ab%20");
        Assert.Equal(HttpStatusCode.OK, twoCharacters.StatusCode);
        Assert.Equal(("ab", 1, 20), context.Provider.LastSearch!.Value);
    }

    [Fact]
    public async Task Search_PaginatesAndNeverPersists_AndReturnsOnlyContractFields()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var first = (await client.GetFromJsonAsync<JsonDocument>("/api/games/search?q=WiTcHeR&pageSize=2"))!;
        var page = first.RootElement;
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(new[] { "coverUrl", "externalId", "name", "platforms", "releaseYear", "slug" },
            page.GetProperty("items")[0].EnumerateObject().Select(property => property.Name).Order().ToArray());
        using var last = (await client.GetFromJsonAsync<JsonDocument>("/api/games/search?q=witcher&page=2&pageSize=2"))!;
        Assert.False(last.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Single(last.RootElement.GetProperty("items").EnumerateArray());
        using var beyond = (await client.GetFromJsonAsync<JsonDocument>("/api/games/search?q=witcher&page=2147483647&pageSize=40"))!;
        Assert.Empty(beyond.RootElement.GetProperty("items").EnumerateArray());
        Assert.False(beyond.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Empty(await context.ReadGamesAsync());
        Assert.Equal(0, context.Provider.DetailCalls);
    }

    [Fact]
    public async Task Search_SpecialCharactersArePreserved_AndDoNotBecomeOtherParameters()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        const string query = "\"acción & amigos\" #café + ?page=99";
        using var response = await client.GetAsync("/api/games/search?q=" + Uri.EscapeDataString(query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((query, 1, 20), context.Provider.LastSearch!.Value);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Fact]
    public async Task Search_MapsYearNulls_AndLimitsPlatformsToFiveNames()
    {
        using var context = new GamesApiTestContext();
        context.Provider.SearchResult = new GameSearchResult([
            new GameSummary(42, "Example", "example", null, null, ["1", "2", "3", "4", "5", "6"]),
            new GameSummary(43, "Other", "other", "https://images.example/art.jpg", new DateOnly(2020, 2, 29), []),
        ], 1, 20, false);
        using var client = await context.AuthenticatedClientAsync();
        using var result = (await client.GetFromJsonAsync<JsonDocument>("/api/games/search?q=example"))!;
        var items = result.RootElement.GetProperty("items");
        Assert.Equal(5, items[0].GetProperty("platforms").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("releaseYear").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("coverUrl").ValueKind);
        Assert.Equal(2020, items[1].GetProperty("releaseYear").GetInt32());
    }

    [Fact]
    public async Task Detail_PersistsLocalIdAndPlainSummary_AndSecondCallDoesNotDuplicateOrFetch()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var first = (await client.GetFromJsonAsync<JsonDocument>("/api/games/19"))!;
        using var again = (await client.GetFromJsonAsync<JsonDocument>("/api/games/19"))!;
        var game = first.RootElement;
        var stored = Assert.Single(await context.ReadGamesAsync());
        Assert.Equal(stored.Id, game.GetProperty("id").GetInt32());
        Assert.NotEqual(19, stored.Id);
        Assert.Equal(19, game.GetProperty("externalId").GetInt32());
        Assert.Equal(stored.Id, again.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Cyberpunk 2077", game.GetProperty("name").GetString());
        Assert.Equal(stored.Summary, game.GetProperty("summary").GetString());
        Assert.DoesNotContain("<", game.GetProperty("summary").GetString());
        Assert.Equal("2018-01-01", game.GetProperty("releaseDate").GetString());
        Assert.Equal(JsonValueKind.Null, game.GetProperty("coverUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, game.GetProperty("coverUrlLarge").ValueKind);
        Assert.Equal(new[] { "coverUrl", "coverUrlLarge", "developers", "externalId", "genres", "id", "name", "platforms", "releaseDate", "slug", "summary" },
            game.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(1, context.Provider.DetailCalls);
    }

    [Fact]
    public async Task ConcurrentDetails_AreSuccessfulAndPersistExactlyOneLocalGame()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var client = await context.AuthenticatedClientAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.GetAsync("/api/games/19")));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var stored = Assert.Single(await context.ReadGamesAsync());
            foreach (var response in responses)
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(stored.Id, body.RootElement.GetProperty("id").GetInt32());
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    [InlineData("1.5")]
    public async Task InvalidId_Returns400WithoutProviderOrPersistence(string id)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/games/" + id);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_game_id");
        Assert.Equal(0, context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Fact]
    public async Task MissingGame_Returns404AndDoesNotPersist()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/games/999999");
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "game_not_found");
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Theory]
    [InlineData("/api/games/search?q=witcher")]
    [InlineData("/api/games/19")]
    public async Task ProviderUnavailable_WithoutLocalCopy_Returns503(string path)
    {
        using var context = new GamesApiTestContext();
        context.Provider.Unavailable = true;
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync(path);
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "catalog_unavailable");
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderUnavailable_WithFreshOrStaleCopy_Returns200AndPreservesLocalId(bool stale)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var first = await client.GetAsync("/api/games/19");
        if (stale) await context.MakeStaleAsync();
        var original = Assert.Single(await context.ReadGamesAsync());
        context.Provider.Unavailable = true;
        using var response = await client.GetAsync("/api/games/19");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(original.Id, result.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(original.UpdatedAt, Assert.Single(await context.ReadGamesAsync()).UpdatedAt);
        Assert.Equal(stale ? 2 : 1, context.Provider.DetailCalls);
    }

    [Fact]
    public async Task MissingGameDuringRefresh_Returns404AndKeepsStoredCopy()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var first = await client.GetAsync("/api/games/19");
        await context.MakeStaleAsync();
        context.Provider.Missing = true;
        using var response = await client.GetAsync("/api/games/19");
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "game_not_found");
        Assert.Single(await context.ReadGamesAsync());
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
