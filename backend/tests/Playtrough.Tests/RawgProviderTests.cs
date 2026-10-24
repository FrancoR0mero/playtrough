using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class RawgProviderTests
{
    private const string EmptySearch = """{"results":[],"next":null}""";
    private const string Game = """{"id":42,"name":"Example","slug":"example"}""";

    [Fact]
    public async Task Search_EscapesKeyAndSpecialCharacters_AndUsesDocumentedParameters()
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(EmptySearch)));
        const string query = "  \"acción & amigos\" #test + café?key=otro  ";
        await context.Provider.SearchAsync(query, 2, 40);
        var uri = Assert.Single(context.Handler.Requests);
        Assert.Equal("https://api.rawg.io/api/games", uri.GetLeftPart(UriPartial.Path));
        Assert.Empty(uri.Fragment);
        var parameters = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal(5, parameters.Count);
        Assert.Equal(RawgTestContext.Key, Assert.Single(parameters["key"]));
        Assert.Equal(query.Trim().ToLowerInvariant(), Assert.Single(parameters["search"]));
        Assert.Equal("2", parameters["page"]);
        Assert.Equal("40", parameters["page_size"]);
        Assert.Equal("true", parameters["exclude_additions"]);
        Assert.False(parameters.ContainsKey("injected"));
    }

    [Fact]
    public async Task Search_MapsNestedPlatformsDatesMissingImages_AndNextWithoutFollowingItsUrl()
    {
        const string body = """
            {"next":"https://untrusted.example/games?key=never-follow", "results":[
              {"id":1,"name":"First","slug":"first","released":"2020-02-29","background_image":null,
               "platforms":[{"platform":{"name":"PC"}},{"platform":{"name":"Switch"}},{"platform":{"name":"PC"}},null,{}]},
              {"id":2,"name":"Second","slug":"second","released":null,"platforms":null},
              {"id":3,"name":"Third","slug":"third","released":"invalid","background_image":"https://media.rawg.io/media/games/example.jpg"}
            ]}
            """;
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(body)));
        var result = await context.Provider.SearchAsync("example", 1, 20);
        Assert.True(result.HasMore);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(new DateOnly(2020, 2, 29), result.Items[0].ReleaseDate);
        Assert.Equal(["PC", "Switch"], result.Items[0].Platforms);
        Assert.Null(result.Items[0].CoverUrl);
        Assert.Null(result.Items[1].ReleaseDate);
        Assert.Empty(result.Items[1].Platforms);
        Assert.Null(result.Items[2].ReleaseDate);
        Assert.Equal("https://media.rawg.io/media/games/example.jpg", result.Items[2].CoverUrl);
        Assert.Equal(1, context.Handler.Count);
    }

    [Fact]
    public async Task Detail_UsesPlainTextOnly_TruncatesAndMapsNamesAndOriginalImage()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = 42, name = "Example", slug = "example", released = "2021-01-02",
            description_raw = new string('á', 3100), description = "<script>unsafe()</script>",
            background_image = "https://media.rawg.io/media/games/example.jpg",
            platforms = new[] { new { platform = new { name = "PC" } } },
            genres = new[] { new { name = "Adventure" } }, developers = new[] { new { name = "Studio" } },
        });
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(body)));
        var game = Assert.IsType<GameDetails>(await context.Provider.GetByExternalIdAsync(42));
        Assert.Equal(new string('á', 3000), game.Summary);
        Assert.Equal(["Adventure"], game.Genres);
        Assert.Equal(["Studio"], game.Developers);
        Assert.Equal(["PC"], game.Platforms);
        Assert.Equal(new DateOnly(2021, 1, 2), game.ReleaseDate);
        Assert.Equal(game.CoverUrl, game.CoverUrlLarge);
        var uri = Assert.Single(context.Handler.Requests);
        Assert.Equal("/api/games/42", uri.AbsolutePath);
        Assert.Equal(RawgTestContext.Key, QueryHelpers.ParseQuery(uri.Query)["key"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"description_raw\":null,\"description\":\"<b>never use HTML</b>\",\"genres\":[],\"developers\":[],\"platforms\":[]}")]
    public async Task Detail_MissingOrNullOptionalFields_AreSafe(string fields)
    {
        var optional = fields[1..^1];
        var body = Game[..^1] + (optional.Length > 0 ? "," + optional : "") + "}";
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(body)));
        var game = Assert.IsType<GameDetails>(await context.Provider.GetByExternalIdAsync(42));
        Assert.Equal("", game.Summary);
        Assert.Empty(game.Genres);
        Assert.Empty(game.Developers);
        Assert.Empty(game.Platforms);
        Assert.Null(game.CoverUrl);
        Assert.Null(game.CoverUrlLarge);
        Assert.Null(game.ReleaseDate);
    }

    [Fact]
    public async Task Detail_TruncationDoesNotSplitASurrogatePair()
    {
        var body = Game[..^1] + ",\"description_raw\":" + JsonSerializer.Serialize(new string('a', 2999) + "🎮extra") + "}";
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(body)));
        Assert.Equal(2999, (await context.Provider.GetByExternalIdAsync(42))!.Summary.Length);
    }

    [Fact]
    public async Task Detail_404IsMissing_WhileSearch404IsUnavailable()
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await context.Provider.GetByExternalIdAsync(42));
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("example", 1, 20));
        Assert.Equal(2, context.Handler.Count);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"results\":null}")]
    [InlineData("{\"results\":[null]}")]
    [InlineData("{\"results\":[{\"id\":0,\"name\":\"x\",\"slug\":\"x\"}]}")]
    public async Task MalformedSearchResponse_BecomesSafeProviderException(string body)
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(body)));
        var error = await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("example", 1, 20));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(body, error.Message);
    }

    [Theory]
    [InlineData("x", 1, 20)]
    [InlineData("example", 0, 20)]
    [InlineData("example", 1, 41)]
    [InlineData("example", 1, 0)]
    public async Task InvalidProviderArguments_DoNotSendRequests(string query, int page, int pageSize)
    {
        using var context = new RawgTestContext((_, _, _) => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => context.Provider.SearchAsync(query, page, pageSize));
        Assert.Equal(0, context.Handler.Count);
    }
}
