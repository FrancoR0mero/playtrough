using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class TimelineLikesEndpointsTests
{
    [Fact]
    public async Task OnlyLikeIdentitiesArePublicAcrossInteractionResponses()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var disliker = await context.AuthenticatedClientAsync();
        using var viewer = await context.AuthenticatedClientAsync();
        using var request = await viewer.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_1" });
        request.EnsureSuccessStatusCode();
        var friendRequest = (await request.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        using var accepted = await owner.PostAsync($"/api/friends/requests/{friendRequest.Id}/accept", null);
        accepted.EnsureSuccessStatusCode();

        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        var reviewList = $"/api/games/{game.Id}/reviews";
        using var reviewResponse = await owner.PutAsJsonAsync(reviewList + "/me", new { rating = 4.5m, text = "Reseña de prueba" });
        Assert.Equal(HttpStatusCode.Created, reviewResponse.StatusCode);
        var review = (await reviewResponse.Content.ReadFromJsonAsync<UpsertReviewResponse>())!.Review;
        var own = "/api/timeline/me";
        var id = Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>(own))!.Items).Id;
        var entry = $"/api/timeline/entries/{id}";
        var comments = entry + "/comments";
        using var added = await owner.PostAsJsonAsync(comments, new { text = "Comentario de prueba" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var comment = (await added.Content.ReadFromJsonAsync<CommentCreatedDto>())!.Comment;
        var likePath = entry + "/likes";
        foreach (var path in new[] { entry + "/reaction", $"/api/timeline/comments/{comment.Id}/reaction", $"{reviewList}/{review.Id}/reaction" })
        {
            using var disliked = await disliker.PutAsJsonAsync(path, new { value = "dislike" });
            disliked.EnsureSuccessStatusCode();
            using var summary = (await disliked.Content.ReadFromJsonAsync<JsonDocument>())!;
            AssertKeys(summary.RootElement, "dislikeCount", "likeCount", "myReaction");
        }
        using var liked = await viewer.PutAsJsonAsync(entry + "/reaction", new { value = "like" });
        liked.EnsureSuccessStatusCode();
        using var listing = (await owner.GetFromJsonAsync<JsonDocument>(likePath))!;
        AssertKeys(listing.RootElement, "hasMore", "items", "nextCursor");
        var item = Assert.Single(listing.RootElement.GetProperty("items").EnumerateArray());
        AssertKeys(item, "createdAt", "user");
        AssertKeys(item.GetProperty("user"), "avatarUrl", "username");
        Assert.Equal("games_user_3", item.GetProperty("user").GetProperty("username").GetString());

        foreach (var (client, path) in new[]
        {
            (owner, own), (viewer, "/api/users/games_user_1/timeline"), (viewer, "/api/feed"),
            (viewer, comments), (viewer, reviewList + "?sort=helpful"), (viewer, entry),
        })
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            using var json = (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
            var raw = json.RootElement.GetRawText();
            Assert.DoesNotContain("games_user_2", raw);
            var row = json.RootElement.TryGetProperty("items", out var rows) ? Assert.Single(rows.EnumerateArray()) : json.RootElement;
            Assert.Equal(1, row.GetProperty("dislikeCount").GetInt32());
            Assert.Equal(path is "/api/feed" or "/api/users/games_user_1/timeline" || path == entry ? "like" : null,
                row.GetProperty("myReaction").GetString());
        }

        using var switched = await viewer.PutAsJsonAsync(entry + "/reaction", new { value = "dislike" });
        switched.EnsureSuccessStatusCode();
        Assert.Empty((await owner.GetFromJsonAsync<TimelineLikesPageDto>(likePath))!.Items);
        using var disapproved = await owner.GetAsync(entry + "/dislikes");
        Assert.Equal(HttpStatusCode.NotFound, disapproved.StatusCode);
    }

    [Fact]
    public async Task LikesRequiresAuthenticationAndSharesTimelineReadBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var owner = await context.AuthenticatedClientAsync();
        var id = await CreateEntryAsync(owner);
        var path = $"/api/timeline/entries/{id}/likes";
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.TimelineRead = new(1, TimeSpan.FromMinutes(1)))));
        using var guest = factory.CreateClient();
        using var actor = factory.CreateClient();
        actor.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        using var unauthorized = await guest.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var allowed = await actor.GetAsync(path);
        allowed.EnsureSuccessStatusCode();
        using var limited = await actor.GetAsync("/api/timeline/me");
        await ProfileEndpointsTests.Error(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task LikeListUsesStableErrorsAndSwaggerDocumentsTheContract()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var owner = await context.AuthenticatedClientAsync();
        var id = await CreateEntryAsync(owner);
        var path = $"/api/timeline/entries/{id}/likes";
        foreach (var query in new[] { "cursor=", "cursor=bad", "cursor=v2.abc", "cursor=a&cursor=b" })
        {
            using var invalid = await owner.GetAsync(path + "?" + query);
            await ProfileEndpointsTests.Error(invalid, 400, "invalid_cursor");
        }
        foreach (var query in new[] { "limit=abc", "limit=0", "limit=51", "limit=1&limit=2" })
        {
            using var invalid = await owner.GetAsync(path + "?" + query);
            await ProfileEndpointsTests.Error(invalid, 400, "invalid_filter");
        }
        using var missing = await owner.GetAsync("/api/timeline/entries/9223372036854775807/likes");
        await ProfileEndpointsTests.Error(missing, 404, "entry_not_found");
        using var swagger = (await owner.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var operation = swagger.RootElement.GetProperty("paths").GetProperty("/api/timeline/entries/{id}/likes").GetProperty("get");
        Assert.Equal("Timeline", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
        Assert.Contains("timeline-read", operation.GetProperty("description").GetString());
        var responses = operation.GetProperty("responses");
        Assert.Contains("entry_not_found", responses.GetProperty("404").GetProperty("description").GetString());
        Assert.False(responses.TryGetProperty("403", out _));
        Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        var limit = Assert.Single(operation.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "limit").GetProperty("schema");
        Assert.Equal(20, limit.GetProperty("default").GetInt32());
        Assert.Equal(50, limit.GetProperty("maximum").GetInt32());
    }

    private static void AssertKeys(JsonElement element, params string[] names)
        => Assert.Equal(names, element.EnumerateObject().Select(property => property.Name).Order().ToArray());

    private static async Task<long> CreateEntryAsync(HttpClient owner)
    {
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        using var added = await owner.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" });
        added.EnsureSuccessStatusCode();
        return Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).Id;
    }
}
