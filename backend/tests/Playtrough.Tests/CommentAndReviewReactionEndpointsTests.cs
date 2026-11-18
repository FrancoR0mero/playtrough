using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class CommentAndReviewReactionEndpointsTests
{
    [Fact]
    public async Task BothRoutesUseTheTokenAndReviewsAcceptHelpfulSort()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var viewer = await context.AuthenticatedClientAsync();
        var (gameId, reviewId, commentId) = await SeedAsync(owner);
        var review = $"/api/games/{gameId}/reviews/{reviewId}/reaction";
        var comment = $"/api/timeline/comments/{commentId}/reaction";
        foreach (var path in new[] { review, comment })
        {
            using var body = new StringContent("{", Encoding.UTF8, "application/json");
            using var invalid = await viewer.PutAsync(path, body);
            await ProfileEndpointsTests.Error(invalid, 400, "invalid_reaction");
            using var own = await owner.PutAsJsonAsync(path, new { value = "like", userId = 2 });
            await ProfileEndpointsTests.Error(own, 409, "cannot_react_to_own_content");
            using var added = await viewer.PutAsJsonAsync(path, new { value = "dislike", userId = 1 });
            added.EnsureSuccessStatusCode();
            Assert.Equal(new ReactionSummaryDto("dislike", 0, 1), await added.Content.ReadFromJsonAsync<ReactionSummaryDto>());
        }

        using var wrongGame = await viewer.DeleteAsync($"/api/games/{gameId + 1}/reviews/{reviewId}/reaction");
        await ProfileEndpointsTests.Error(wrongGame, 404, "review_not_found");
        var result = (await viewer.GetFromJsonAsync<ReviewListResponse>($"/api/games/{gameId}/reviews?sort=helpful"))!;
        var row = Assert.Single(result.Items);
        Assert.Equal((reviewId, 0, 1, "dislike"), (row.Id, row.LikeCount, row.DislikeCount, row.MyReaction));
        Assert.Equal(4.5m, result.RatingSummary.Average);

        foreach (var path in new[] { review, comment })
        {
            using var response = await viewer.DeleteAsync(path);
            response.EnsureSuccessStatusCode();
            Assert.Equal(new ReactionSummaryDto(null, 0, 0), await response.Content.ReadFromJsonAsync<ReactionSummaryDto>());
        }
    }

    [Fact]
    public async Task BothRoutesRequireAuthenticationAndShareTheExistingReactionBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var viewer = await context.AuthenticatedClientAsync();
        var (gameId, reviewId, commentId) = await SeedAsync(owner);
        var review = $"/api/games/{gameId}/reviews/{reviewId}/reaction";
        var comment = $"/api/timeline/comments/{commentId}/reaction";
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(options => options.ReactionsWrite = new(1, TimeSpan.FromMinutes(1)))));
        using var actor = factory.CreateClient();
        using var guest = factory.CreateClient();
        actor.DefaultRequestHeaders.Authorization = viewer.DefaultRequestHeaders.Authorization;
        foreach (var path in new[] { review, comment })
        {
            using var put = await guest.PutAsJsonAsync(path, new { value = "like" });
            using var delete = await guest.DeleteAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        }
        using var added = await actor.PutAsJsonAsync(comment, new { value = "like" });
        added.EnsureSuccessStatusCode();
        using var limited = await actor.PutAsJsonAsync(review, new { value = "like" });
        await ProfileEndpointsTests.Error(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task SwaggerLabelsTheNewRoutesAndTheirErrors()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var (path, code) in new[]
        {
            ("/api/games/{gameId}/reviews/{reviewId}/reaction", "review_not_found"),
            ("/api/timeline/comments/{id}/reaction", "comment_not_found"),
        })
        {
            foreach (var verb in new[] { "put", "delete" })
            {
                var action = paths.GetProperty(path).GetProperty(verb);
                Assert.Equal("Reactions", Assert.Single(action.GetProperty("tags").EnumerateArray()).GetString());
                Assert.Contains(code, action.GetProperty("responses").GetProperty("404").GetProperty("description").GetString());
            }
        }
    }

    private static async Task<(int GameId, int ReviewId, long CommentId)> SeedAsync(HttpClient owner)
    {
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        using var posted = await owner.PutAsJsonAsync($"/api/games/{game.Id}/reviews/me", new { rating = 4.5m, text = "Reseña" });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var review = (await posted.Content.ReadFromJsonAsync<UpsertReviewResponse>())!.Review;
        var entry = Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items);
        using var response = await owner.PostAsJsonAsync($"/api/timeline/entries/{entry.Id}/comments", new { text = "Comentario" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var comment = (await response.Content.ReadFromJsonAsync<CommentCreatedDto>())!.Comment;
        return (game.Id, review.Id, comment.Id);
    }
}
