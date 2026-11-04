using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Playtrough.Api.Catalog;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Tests;

public sealed class GameBrainTests
{
    private const string Full = """
        {"id":42,"name":"Example","link":"https://gamebrain.co/game/example","release_date":"2020-02-29",
        "image":"https://images.example/game.jpg","description":"Plain text","platforms":[{"name":"PC","value":"pc"}],
        "genres":[{"name":"Action"}],"tags":[{"name":"Indie"}],"developer":"Studio",
        "rating":{"mean":0.9,"count":12,"mean_critics":0.85},"screenshots":["https://images.example/shot.jpg"],
        "playtime":{"median":12},"adult_only":false,
        "offers":[{"store_name":"Steam","price":{"value":10,"initial":20,"discount_percent":50,"currency":"USD"}}]}
        """;

    [Fact]
    public void Mapper_MapsCompleteResponse_WithoutMixingLocalRatingsOrInventingSlug()
    {
        using var json = JsonDocument.Parse(Full);
        var game = GameBrainMapper.Map(json.RootElement);
        Assert.Equal(4.5m, game.ProviderRating);
        Assert.Equal(85m, game.CriticScore);
        Assert.Equal(12, game.ProviderRatingCount);
        Assert.Equal("", game.Slug);
        Assert.Equal("https://gamebrain.co/game/example", game.SourceUrl);
        Assert.Equal(new DateOnly(2020, 2, 29), game.ReleaseDate);
        Assert.Equal(["Studio"], game.Developers);
        Assert.Equal(["PC"], game.Platforms);
        Assert.Single(game.Screenshots);
        Assert.Equal(12m, game.PlaytimeHours);
        Assert.False(game.AdultOnly);
        Assert.Equal(new GameOffer("Steam", 10, 20, 50, "USD"), Assert.Single(game.Offers));
    }

    [Fact]
    public void Mapper_NullsAndYear_DoNotInventReleaseDate()
    {
        using var json = JsonDocument.Parse("""{"id":1,"name":"Minimal","year":2024,"rating":null,"platforms":null,"developer":null} """);
        var game = GameBrainMapper.Map(json.RootElement);
        Assert.Null(game.ReleaseDate);
        Assert.Equal(2024, game.ReleaseYear);
        Assert.Null(game.ProviderRating);
        Assert.Null(game.CriticScore);
        Assert.Null(game.AdultOnly);
        Assert.Empty(game.Platforms);
        Assert.Empty(game.Developers);
        Assert.Empty(game.Screenshots);
        Assert.Empty(game.Offers);
    }

    [Fact]
    public async Task Client_UsesHeaderAndOffset_AndHonorsCap()
    {
        using var fixture = new Fixture((request, _) => {
            Assert.Equal("private-test-key", Assert.Single(request.Headers.GetValues("x-api-key")));
            Assert.DoesNotContain("key", request.RequestUri!.Query);
            Assert.Contains("offset=20", request.RequestUri.Query);
            Assert.Contains("limit=20", request.RequestUri.Query);
            return Json("""{"total_results":100,"results":[]} """);
        });
        Assert.True((await fixture.Client.SearchAsync("Example", 2, 20)).HasMore);
        Assert.True((await fixture.Client.SearchAsync("Example", int.MaxValue, 20)).IsLimited);
        Assert.Equal(1, fixture.Calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Client_RejectsCredentialsWithoutRetry(int status)
    {
        using var fixture = new Fixture((_, _) => new((HttpStatusCode)status));
        await Assert.ThrowsAsync<GameBrainAuthenticationException>(() => fixture.Client.GetByExternalIdAsync(42));
        Assert.Equal(1, fixture.Calls);
    }

    [Fact]
    public async Task Client_NotFoundIsNotRetried()
    {
        using var fixture = new Fixture((_, _) => new(HttpStatusCode.NotFound));
        Assert.Null(await fixture.Client.MatchAsync(new(null, null, "Unknown", 2020)));
        Assert.Null(await fixture.Client.GetByExternalIdAsync(42));
        Assert.Equal(2, fixture.Calls);
    }

    [Fact]
    public async Task Client_QuotaWithoutCacheReturnsLimitedSearch_AndMatchStops()
    {
        using var fixture = new Fixture((_, _) => new(HttpStatusCode.PaymentRequired));
        Assert.True((await fixture.Client.SearchAsync("Example", 1, 20)).IsLimited);
        await Assert.ThrowsAsync<GameBrainQuotaException>(() => fixture.Client.MatchAsync(new(42, null, null, null)));
    }

    [Fact]
    public async Task Client_Retries429_AndCachesSuccessfulDetail()
    {
        using var fixture = new Fixture((_, attempt) => attempt < 3 ? new(HttpStatusCode.TooManyRequests) : Json(Full));
        Assert.Equal(42, (await fixture.Client.GetByExternalIdAsync(42))!.ExternalId);
        Assert.Equal(42, (await fixture.Client.GetByExternalIdAsync(42))!.ExternalId);
        Assert.Equal(3, fixture.Calls);
    }

    [Fact]
    public async Task Client_QuotaFallbackExpires_AndDoesNotRenewLifetime()
    {
        using var fixture = new Fixture((_, attempt) => attempt == 1 ? Json("""{"total_results":1,"results":[{"id":42,"name":"Example"}]}""") : new(HttpStatusCode.PaymentRequired));
        await fixture.Client.SearchAsync("Example", 1, 20);
        fixture.CacheClock.UtcNow += TimeSpan.FromMinutes(11);
        Assert.Single((await fixture.Client.SearchAsync("Example", 1, 20)).Items);
        fixture.CacheClock.UtcNow += TimeSpan.FromHours(24);
        var expired = await fixture.Client.SearchAsync("Example", 1, 20);
        Assert.Empty(expired.Items);
        Assert.True(expired.IsLimited);
    }

    [Fact]
    public async Task Client_SuggestionsUseTheirOwnEndpoint_AndMaximumTen()
    {
        using var fixture = new Fixture((request, _) => {
            Assert.Contains("games/suggestions?", request.RequestUri!.ToString());
            return Json("""{"results":[{"id":42,"name":"Example"}]}""");
        });
        Assert.Single((await fixture.Client.SuggestAsync("Ex", 10)).Items);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.SuggestAsync("Ex", 11));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache memory;
        private readonly HttpClient http;
        private readonly GameBrainGate gate;
        public TestCacheClock CacheClock { get; } = new();
        public GameBrainClient Client { get; }
        public int Calls;
        public Fixture(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            memory = new MemoryCache(new MemoryCacheOptions { Clock = CacheClock });
            var time = new AdvancingCatalogTime();
            gate = new(time);
            http = new HttpClient(new CatalogHttpHandler((request, _, _) => Task.FromResult(respond(request, ++Calls))))
                { BaseAddress = new Uri("https://api.gamebrain.co/v1/") };
            Client = new(http, Options.Create(new GameBrainOptions { ApiKey = "private-test-key" }), gate,
                new GameCatalogCache(memory, Mock.Of<IHostApplicationLifetime>()), memory, time, NullLogger<GameBrainClient>.Instance);
        }
        public void Dispose() { memory.Dispose(); http.Dispose(); gate.Dispose(); }
    }
}
