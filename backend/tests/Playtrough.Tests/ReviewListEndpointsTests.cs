using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class ReviewListEndpointsTests
{
    [Fact]
    public async Task List_RequiresAuthentication_WithoutImportingGame()
    {
        using var context = new GamesApiTestContext();
        using var client = context.Factory.CreateClient();
        using var response = await client.GetAsync(PathFor(19));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task EmptyGame_AnyRole_ReturnsEmptyListAndNullAverage(string role)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync(role);
        var game = await ReviewEndpointsTests.OpenGameAsync(client);
        var result = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(game.Id)))!;
        Assert.Empty(result.Items);
        Assert.Equal(new RatingSummary(null, 0), result.RatingSummary);
        Assert.Equal(game.RatingSummary, result.RatingSummary);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.False(result.HasMore);
        Assert.Equal(1, context.Provider.DetailCalls);
    }

    [Theory]
    [InlineData("recent", new[] { 7, 4, 2, 1, 6, 3, 5, 0 })]
    [InlineData("top", new[] { 6, 3, 4, 1, 7, 2, 0, 5 })]
    [InlineData("low", new[] { 5, 2, 0, 7, 4, 1, 6, 3 })]
    public async Task EveryOrder_TiesAreDeterministic_AndPaginationHasNoDuplicatesOrOmissions(string sort, int[] order)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var (gameId, rows) = await SeedAsync(context, client);
        var actual = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = (await client.GetFromJsonAsync<ReviewListResponse>($"{PathFor(gameId)}?sort={sort}&page={page}&pageSize=3"))!;
            Assert.Equal(page, result.Page);
            Assert.Equal(3, result.PageSize);
            Assert.Equal(page != 3, result.HasMore);
            Assert.Equal(new RatingSummary(3.6m, 8), result.RatingSummary);
            actual.AddRange(result.Items.Select(item => item.Id));
        }
        Assert.Equal(order.Select(index => rows[index].Id).ToArray(), actual.ToArray());
        Assert.Equal(rows.Length, actual.Distinct().Count());
        var beyond = (await client.GetFromJsonAsync<ReviewListResponse>($"{PathFor(gameId)}?sort={sort}&page=4&pageSize=3"))!;
        Assert.Empty(beyond.Items);
        Assert.False(beyond.HasMore);
        Assert.Equal(new RatingSummary(3.6m, 8), beyond.RatingSummary);
    }

    [Fact]
    public async Task DefaultsAndMaximumPageSize_AreApplied_AndLargeOffsetsDoNotOverflow()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var (gameId, _) = await SeedAsync(context, client, count: 31);
        var defaults = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId)))!;
        var explicitRecent = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?sort=recent&page=1&pageSize=10"))!;
        Assert.Equal(1, defaults.Page);
        Assert.Equal(10, defaults.PageSize);
        Assert.Equal(10, defaults.Items.Count);
        Assert.True(defaults.HasMore);
        Assert.Equal(explicitRecent.Items.Select(item => item.Id).ToArray(), defaults.Items.Select(item => item.Id).ToArray());
        var maximum = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?pageSize=30"))!;
        var last = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?page=2&pageSize=30"))!;
        Assert.Equal(30, maximum.Items.Count);
        Assert.True(maximum.HasMore);
        Assert.Single(last.Items);
        Assert.False(last.HasMore);
        Assert.Equal(31, maximum.Items.Concat(last.Items).Select(item => item.Id).Distinct().Count());
        var huge = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?page=2147483647&pageSize=30"))!;
        Assert.Empty(huge.Items);
        Assert.False(huge.HasMore);
        Assert.Equal(defaults.RatingSummary, huge.RatingSummary);
    }

    [Fact]
    public async Task PublicProjection_OnlyExposesUsernameAndId_AndIsMineAlwaysUsesToken()
    {
        using var context = new GamesApiTestContext();
        using var first = await context.AuthenticatedClientAsync();
        using var second = await context.AuthenticatedClientAsync("Admin");
        using var spectator = await context.AuthenticatedClientAsync("Moderator");
        var (gameId, rows) = await SeedAsync(context, first);
        var firstList = (await first.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?pageSize=30"))!;
        var secondList = (await second.GetFromJsonAsync<ReviewListResponse>($"{PathFor(gameId)}?pageSize=30&userId={rows[0].UserId}"))!;
        var spectatorList = (await spectator.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?pageSize=30"))!;
        Assert.Equal(rows[0].Id, Assert.Single(firstList.Items, item => item.IsMine).Id);
        Assert.Equal(rows[1].Id, Assert.Single(secondList.Items, item => item.IsMine).Id);
        Assert.All(spectatorList.Items, item => Assert.False(item.IsMine));
        Assert.Equal(firstList.Items.Select(item => item.Id).ToArray(), secondList.Items.Select(item => item.Id).ToArray());
        Assert.Null(Assert.Single(firstList.Items, item => item.IsMine).Text); // Rating-only review is listed.
        Assert.Equal(DateTimeKind.Utc, firstList.Items[0].CreatedAt.Kind);
        using var response = await first.GetAsync(PathFor(gameId) + "?pageSize=30");
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "hasMore", "items", "page", "pageSize", "ratingSummary" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(new[] { "author", "createdAt", "editedAt", "id", "isMine", "rating", "text" },
                item.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal(new[] { "userId", "username" }, item.GetProperty("author").EnumerateObject()
                .Select(property => property.Name).Order().ToArray());
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("author").GetProperty("username").GetString()));
        }
        Assert.DoesNotContain("private-email-marker", json.RootElement.GetRawText());
        Assert.DoesNotContain("@example.com", json.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("?sort=best")]
    [InlineData("?sort=Recent")]
    [InlineData("?sort=top%20")]
    [InlineData("?sort=")]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?page=abc")]
    [InlineData("?page=1.5")]
    [InlineData("?page=2147483648")]
    [InlineData("?page=")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=-1")]
    [InlineData("?pageSize=31")]
    [InlineData("?pageSize=abc")]
    [InlineData("?pageSize=1.5")]
    [InlineData("?pageSize=2147483648")]
    [InlineData("?pageSize=")]
    public async Task InvalidParameters_Are400InvalidQuery(string query)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync(PathFor(19) + query);
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_query");
        Assert.Equal(0, context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(19)]
    [InlineData(int.MaxValue)]
    public async Task MissingLocalGame_Is404GameNotFound_WithoutImport(int id)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync(PathFor(id));
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "game_not_found");
        Assert.Equal(0, context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Fact]
    public async Task Summary_IsGlobalToGame_AndMatchesDetailAndRecalculationAfterEditsAndDelete()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var (gameId, _) = await SeedAsync(context, client);
        foreach (var rating in new[] { 4.5m, 5m })
        {
            using var edited = await client.PutAsJsonAsync(ReviewEndpointsTests.PathFor(gameId), new { rating });
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var mutation = (await edited.Content.ReadFromJsonAsync<UpsertReviewResponse>())!;
            await AssertSummaries(mutation.RatingSummary);
        }
        using var deleted = await client.DeleteAsync(ReviewEndpointsTests.PathFor(gameId));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        await AssertSummaries((await deleted.Content.ReadFromJsonAsync<DeleteReviewResponse>())!.RatingSummary);

        async Task AssertSummaries(RatingSummary mutationSummary)
        {
            var page = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?pageSize=1"))!;
            var detail = await ReviewEndpointsTests.OpenGameAsync(client);
            var rows = (await ReviewEndpointsTests.ReadReviewsAsync(context)).Where(review => review.GameId == gameId).ToArray();
            var recalculated = RatingSummary.FromTotals(rows.Length, rows.Sum(review => review.Rating));
            Assert.Equal(recalculated, page.RatingSummary);
            Assert.Equal(recalculated, detail.RatingSummary);
            Assert.Equal(recalculated, mutationSummary);
            Assert.Single(page.Items);
            Assert.True(page.HasMore);
            await ReviewEndpointsTests.AssertStateAsync(context, gameId, rows.Select(review => review.Rating).ToArray());
        }
    }

    [Fact]
    public async Task ListingOneGame_DoesNotIncludeReviewsOrSummaryFromAnotherGame()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var (gameId, rows) = await SeedAsync(context, client);
        using var secondGame = (await client.GetFromJsonAsync<JsonDocument>("/api/games/5"))!;
        var otherId = secondGame.RootElement.GetProperty("id").GetInt32();
        using var created = await client.PutAsJsonAsync(ReviewEndpointsTests.PathFor(otherId), new { rating = 1.5m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(gameId) + "?pageSize=30"))!;
        Assert.Equal(rows.Select(review => review.Id).Order().ToArray(), list.Items.Select(item => item.Id).Order().ToArray());
        Assert.Equal(new RatingSummary(3.6m, 8), list.RatingSummary);
        var other = (await client.GetFromJsonAsync<ReviewListResponse>(PathFor(otherId)))!;
        Assert.Single(other.Items);
        Assert.Equal(new RatingSummary(1.5m, 1), other.RatingSummary);
    }

    [Fact]
    public async Task GlobalReadLimit_Returns429WithRetryAfter_AndOtherUserHasIndependentBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var original = await context.AuthenticatedClientAsync();
        using var otherOriginal = await context.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(original);
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.Global = new(1, TimeSpan.FromMinutes(1)))));
        using var client = factory.CreateClient();
        using var other = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        other.DefaultRequestHeaders.Authorization = otherOriginal.DefaultRequestHeaders.Authorization;
        using var allowed = await client.GetAsync(PathFor(game.Id));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var rejected = await client.GetAsync(PathFor(game.Id));
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        using var independent = await other.GetAsync(PathFor(game.Id));
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
    }

    private static string PathFor(int gameId) => $"/api/games/{gameId}/reviews";

    private static async Task<(int GameId, Review[] Rows)> SeedAsync(GamesApiTestContext context, HttpClient client, int count = 8)
    {
        var game = await ReviewEndpointsTests.OpenGameAsync(client);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Users.SingleAsync(user => user.UserName == "games_user_1");
        var second = await db.Users.SingleOrDefaultAsync(user => user.UserName == "games_user_2");
        var authors = new List<ApplicationUser> { owner };
        for (var index = 1; index < count; index++)
        {
            var author = index == 1 && second is not null ? second : new ApplicationUser
            {
                UserName = $"review_author_{index}", NormalizedUserName = $"REVIEW_AUTHOR_{index}",
                Email = $"private-email-marker-{index}@example.com",
            };
            if (author.Id == 0) db.Users.Add(author);
            authors.Add(author);
        }
        await db.SaveChangesAsync();
        decimal[] ratings = [2.5m, 4.5m, 2.5m, 5m, 4.5m, 1m, 5m, 3.5m];
        int[] days = [0, 2, 2, 1, 2, 0, 1, 2];
        var start = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        var rows = authors.Select((author, index) => new Review
        {
            UserId = author.Id, GameId = game.Id, Rating = ratings[index % ratings.Length],
            Text = index % 5 == 0 ? null : $"Reseña ficticia {index}", CreatedAt = start.AddDays(days[index % days.Length]),
            EditedAt = index == 0 ? start.AddDays(5) : null, ConcurrencyToken = Guid.NewGuid(),
        }).ToArray();
        db.Reviews.AddRange(rows);
        var stored = await db.Games.SingleAsync(value => value.Id == game.Id);
        stored.RatingCount = rows.Length;
        stored.RatingSum = rows.Sum(review => review.Rating);
        await db.SaveChangesAsync();
        return (game.Id, rows);
    }
}
