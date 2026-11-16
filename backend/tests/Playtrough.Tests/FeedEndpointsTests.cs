using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class FeedEndpointsTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task AnyRole_SeesOnlyFriendsActivity_WithFlatEntryAndAuthorShape(string role)
    {
        using var c = new GamesApiTestContext();
        using var viewer = await c.AuthenticatedClientAsync(role);
        using var author = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        using var sent = await viewer.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        sent.EnsureSuccessStatusCode();
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await author.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        (await author.PutAsJsonAsync("/api/users/me/privacy", new { visibility = "private" })).EnsureSuccessStatusCode();
        var game = await ReviewEndpointsTests.OpenGameAsync(author);
        (await author.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        using var response = await viewer.GetAsync("/api/feed");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("games_user_2", item.GetProperty("author").GetProperty("username").GetString());
        Assert.True(item.GetProperty("id").GetInt64() > 0);
        Assert.Equal("added", item.GetProperty("type").GetString());
        Assert.Equal("playing", item.GetProperty("toStatus").GetString());
        Assert.False(item.GetProperty("isHidden").GetBoolean());
        Assert.False(item.TryGetProperty("entry", out _));
        Assert.False(item.TryGetProperty("userId", out _));
        Assert.Single((await viewer.GetFromJsonAsync<FeedPageDto>("/api/feed"))!.Items);
        Assert.Empty((await author.GetFromJsonAsync<FeedPageDto>("/api/feed"))!.Items);
        Assert.Empty((await stranger.GetFromJsonAsync<FeedPageDto>("/api/feed"))!.Items);
        (await viewer.DeleteAsync("/api/friends/games_user_2")).EnsureSuccessStatusCode();
        Assert.Empty((await viewer.GetFromJsonAsync<FeedPageDto>("/api/feed"))!.Items);
    }

    [Theory]
    [InlineData("cursor=", "invalid_cursor")]
    [InlineData("cursor=bad", "invalid_cursor")]
    [InlineData("cursor=v2.abc", "invalid_cursor")]
    [InlineData("cursor=v1.a%3D", "invalid_cursor")]
    [InlineData("cursor=a&cursor=b", "invalid_cursor")]
    [InlineData("limit=0", "invalid_filter")]
    [InlineData("limit=51", "invalid_filter")]
    [InlineData("limit=abc", "invalid_filter")]
    [InlineData("limit=1&limit=2", "invalid_filter")]
    public async Task InvalidQuery_UsesStableProblemCodes(string query, string code)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/feed?" + query);
        await ProfileEndpointsTests.Error(response, 400, code);
    }

    [Fact]
    public async Task Feed_SharesTimelineReadBudget_AndAnonymousOrInvalidTokensStay401()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var first = await c.AuthenticatedClientAsync();
        using var second = await c.AuthenticatedClientAsync();
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(s => s.TimelineRead = new(2, TimeSpan.FromMinutes(1)))));
        using var client = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        using var invalid = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
        other.DefaultRequestHeaders.Authorization = second.DefaultRequestHeaders.Authorization;
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/feed")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.GetAsync("/api/feed")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/feed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/timeline/me/periods")).StatusCode);
        using var rejected = await client.GetAsync("/api/feed");
        await ProfileEndpointsTests.Error(rejected, 429, "rate_limited");
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/feed")).StatusCode);
    }

    [Fact]
    public async Task Swagger_DocumentsFeedCursorLimitAndSharedPolicy()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var json = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var get = json.RootElement.GetProperty("paths").GetProperty("/api/feed").GetProperty("get");
        Assert.Equal("Feed", Assert.Single(get.GetProperty("tags").EnumerateArray()).GetString());
        Assert.Contains("timeline-read", get.GetProperty("description").GetString());
        Assert.Contains("invalid_cursor", get.GetProperty("responses").GetProperty("400").GetProperty("description").GetString());
        Assert.True(get.GetProperty("responses").GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        var parameters = get.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(2, parameters.Length);
        var schema = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "limit").GetProperty("schema");
        Assert.Equal(20, schema.GetProperty("default").GetInt32());
        Assert.Equal(50, schema.GetProperty("maximum").GetInt32());
    }
}
