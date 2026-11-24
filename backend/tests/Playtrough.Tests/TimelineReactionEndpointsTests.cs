using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class TimelineReactionEndpointsTests
{
    [Fact]
    public async Task ReactionUsesTheTokenAndExposesOnlyCurrentViewersValue()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var viewer = await context.AuthenticatedClientAsync();
        var id = await CreateEntry(owner);
        var path = PathFor(id);

        using var forbidden = await owner.PutAsJsonAsync(path, new { value = "like", userId = 2 });
        await ProfileEndpointsTests.Error(forbidden, 409, "cannot_react_to_own_content");
        using var created = await viewer.PutAsJsonAsync(path, new { value = "dislike", userId = 1 });
        created.EnsureSuccessStatusCode();
        Assert.Equal(new ReactionSummaryDto("dislike", 0, 1), await created.Content.ReadFromJsonAsync<ReactionSummaryDto>());
        var own = Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items);
        var other = Assert.Single((await viewer.GetFromJsonAsync<TimelinePageDto>("/api/users/games_user_1/timeline"))!.Items);
        Assert.Equal(1, own.DislikeCount);
        Assert.Null(own.MyReaction);
        Assert.Equal("dislike", other.MyReaction);
        using var removed = await viewer.DeleteAsync(path);
        removed.EnsureSuccessStatusCode();
        Assert.Equal(new ReactionSummaryDto(null, 0, 0), await removed.Content.ReadFromJsonAsync<ReactionSummaryDto>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"value\":null}")]
    [InlineData("{\"value\":1}")]
    [InlineData("{\"value\":\"LIKE\"}")]
    [InlineData("{\"value\":\"love\"}")]
    public async Task InvalidBodies_ReturnStableProblemCode(string body)
    {
        using var context = new GamesApiTestContext();
        using var viewer = await context.AuthenticatedClientAsync();
        using var response = await viewer.PutAsync(PathFor(1), new StringContent(body, Encoding.UTF8, "application/json"));
        await ProfileEndpointsTests.Error(response, 400, "invalid_reaction");
    }

    [Fact]
    public async Task BothVerbsRequireAuthenticationAndShareThePerUserWriteBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var viewer = await context.AuthenticatedClientAsync();
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(options => options.ReactionsWrite = new(2, TimeSpan.FromMinutes(1)))));
        using var actor = factory.CreateClient();
        using var guest = factory.CreateClient();
        actor.DefaultRequestHeaders.Authorization = viewer.DefaultRequestHeaders.Authorization;
        var path = PathFor(await CreateEntry(owner));

        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsJsonAsync(path, new { value = "like" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actor.PutAsJsonAsync(path, new { value = "like" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actor.DeleteAsync(path)).StatusCode);
        using var limited = await actor.PutAsJsonAsync(path, new { value = "dislike" });
        await ProfileEndpointsTests.Error(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task SwaggerDocumentsBothVerbsTheirTagAndErrors()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var path = document.RootElement.GetProperty("paths").GetProperty("/api/timeline/entries/{id}/reaction");
        foreach (var verb in new[] { "put", "delete" })
        {
            var operation = path.GetProperty(verb);
            Assert.Equal("Reactions", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
            var responses = operation.GetProperty("responses");
            Assert.True(responses.TryGetProperty("429", out _));
            Assert.True(responses.TryGetProperty("404", out _));
        }
        Assert.True(path.GetProperty("put").GetProperty("responses").TryGetProperty("409", out _));
    }

    private static string PathFor(long id) => $"/api/timeline/entries/{id}/reaction";

    private static async Task<long> CreateEntry(HttpClient owner)
    {
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        return Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).Id;
    }
}
