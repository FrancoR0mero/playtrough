using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Games;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class ReviewEndpointsTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task EveryOwnEndpoint_RequiresRealAuthentication(string method)
    {
        using var context = new GamesApiTestContext();
        using var client = context.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/games/1/reviews/me");
        if (method == "PUT") request.Content = JsonContent.Create(new { rating = 4.5m });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal(0, context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task FullFlow_AnyRole_UsesOwnIdentityAndExactPersistedCounters(string role)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync(role);
        var game = await OpenGameAsync(client);
        var path = PathFor(game.Id);
        Assert.Equal(new RatingSummary(null, 0), game.RatingSummary);
        await AssertStateAsync(context, game.Id);
        using var created = await client.PutAsJsonAsync(path, new { rating = 4.5m, text = "  <b>opinión</b>\r\n🎮\0  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith(path, created.Headers.Location!.ToString());
        var first = (await created.Content.ReadFromJsonAsync<UpsertReviewResponse>())!;
        Assert.Equal(4.5m, first.Review.Rating);
        Assert.Equal("<b>opinión</b>\n🎮", first.Review.Text);
        Assert.Equal(game.Id, first.Review.GameId);
        Assert.Null(first.Review.EditedAt);
        Assert.Equal(DateTimeKind.Utc, first.Review.CreatedAt.Kind);
        Assert.Equal(new RatingSummary(4.5m, 1), first.RatingSummary);
        await AssertStateAsync(context, game.Id, 4.5m);
        Assert.Equal(first.RatingSummary, (await OpenGameAsync(client)).RatingSummary);

        using var edited = await client.PutAsJsonAsync(path, new { rating = 3.5m });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var second = (await edited.Content.ReadFromJsonAsync<UpsertReviewResponse>())!;
        Assert.Equal(first.Review.Id, second.Review.Id);
        Assert.Equal(first.Review.CreatedAt, second.Review.CreatedAt);
        Assert.NotNull(second.Review.EditedAt);
        Assert.Equal(DateTimeKind.Utc, second.Review.EditedAt.Value.Kind);
        Assert.Null(second.Review.Text);
        Assert.Equal(new RatingSummary(3.5m, 1), second.RatingSummary);
        await AssertStateAsync(context, game.Id, 3.5m);
        var beforeRepeat = Assert.Single(await ReadReviewsAsync(context));

        using var repeated = await client.PutAsJsonAsync(path, new { rating = 3.5m, text = " \t\r\n " });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(second, await repeated.Content.ReadFromJsonAsync<UpsertReviewResponse>());
        Assert.Equal(beforeRepeat.ConcurrencyToken, Assert.Single(await ReadReviewsAsync(context)).ConcurrencyToken);
        await AssertStateAsync(context, game.Id, 3.5m);
        Assert.Equal(second.Review, await client.GetFromJsonAsync<MyReviewDto>(path));
        using var mine = (await client.GetFromJsonAsync<JsonDocument>(path))!;
        Assert.Equal(new[] { "createdAt", "editedAt", "gameId", "id", "rating", "text" },
            mine.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(second.RatingSummary, (await OpenGameAsync(client)).RatingSummary);
        Assert.Equal(1, context.Provider.DetailCalls); // Metadata came from the existing local copy.

        using var deleted = await client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(new DeleteReviewResponse(new(null, 0)), await deleted.Content.ReadFromJsonAsync<DeleteReviewResponse>());
        await AssertStateAsync(context, game.Id);
        Assert.Equal(new RatingSummary(null, 0), (await OpenGameAsync(client)).RatingSummary);
        using var missing = await client.GetAsync(path);
        using var missingDelete = await client.DeleteAsync(path);
        await GamesEndpointsTests.AssertProblemAsync(missing, HttpStatusCode.NotFound, "review_not_found");
        await GamesEndpointsTests.AssertProblemAsync(missingDelete, HttpStatusCode.NotFound, "review_not_found");
        using var scope = context.Factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryEntries.ToListAsync());
    }

    [Theory]
    [InlineData("", "invalid_rating")]
    [InlineData("null", "invalid_rating")]
    [InlineData("{}", "invalid_rating")]
    [InlineData("[]", "invalid_rating")]
    [InlineData("42", "invalid_rating")]
    [InlineData("{", "invalid_rating")]
    [InlineData("{\"rating\":null}", "invalid_rating")]
    [InlineData("{\"rating\":\"4.5\"}", "invalid_rating")]
    [InlineData("{\"rating\":true}", "invalid_rating")]
    [InlineData("{\"rating\":[]}", "invalid_rating")]
    [InlineData("{\"rating\":{}}", "invalid_rating")]
    [InlineData("{\"rating\":0}", "invalid_rating")]
    [InlineData("{\"rating\":6}", "invalid_rating")]
    [InlineData("{\"rating\":-1}", "invalid_rating")]
    [InlineData("{\"rating\":4.1}", "invalid_rating")]
    [InlineData("{\"rating\":1e100}", "invalid_rating")]
    [InlineData("{\"rating\":4.5000000000000000000000000000001}", "invalid_rating")]
    [InlineData("{\"rating\":5.0000000000000000000000000000001}", "invalid_rating")]
    [InlineData("{\"rating\":4.5,\"text\":42}", "invalid_text")]
    [InlineData("{\"rating\":4.5,\"text\":true}", "invalid_text")]
    [InlineData("{\"rating\":4.5,\"text\":[]}", "invalid_text")]
    [InlineData("{\"rating\":4.5,\"text\":{}}", "invalid_text")]
    public async Task InvalidJsonBodies_UseOwnCodes_AndLeaveExistingReviewUnchanged(string body, string code)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var game = await OpenGameAsync(client);
        using var created = await client.PutAsJsonAsync(PathFor(game.Id), new { rating = 2.5m, text = "original" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = Assert.Single(await ReadReviewsAsync(context));
        using var response = await client.PutAsync(PathFor(game.Id), new StringContent(body, Encoding.UTF8, "application/json"));
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(error.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(original.ConcurrencyToken, Assert.Single(await ReadReviewsAsync(context)).ConcurrencyToken);
        await AssertStateAsync(context, game.Id, 2.5m);
    }

    [Fact]
    public async Task EmptyBodyWithoutContentType_Is400InvalidRating()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.PutAsync("/api/games/1/reviews/me", null);
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_rating");
        Assert.Empty(await ReadReviewsAsync(context));
    }

    [Theory]
    [InlineData("{\"rating\":4.5}")]
    [InlineData("{\"rating\":4.5,\"text\":null}")]
    [InlineData("{\"rating\":45e-1,\"text\":\" \"}")]
    [InlineData("{\"rating\":4.5000000000000000000000000000000}")]
    public async Task EquivalentNumericRepresentations_AndOptionalText_AreValid(string body)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var game = await OpenGameAsync(client);
        using var response = await client.PutAsync(PathFor(game.Id), new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<UpsertReviewResponse>())!;
        Assert.Equal(4.5m, result.Review.Rating);
        Assert.Null(result.Review.Text);
        await AssertStateAsync(context, game.Id, 4.5m);
    }

    [Fact]
    public async Task TextLength_IsValidatedAfterSanitization_AndAllows2000Emojis()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var game = await OpenGameAsync(client);
        var text = string.Concat(Enumerable.Repeat("🎮", 2000));
        using var valid = await client.PutAsJsonAsync(PathFor(game.Id), new { rating = 4.5m, text = " \0\r\n" + text + "\t " });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        Assert.Equal(text, (await valid.Content.ReadFromJsonAsync<UpsertReviewResponse>())!.Review.Text);
        using var tooLong = await client.PutAsJsonAsync(PathFor(game.Id), new { rating = 1m, text = text + "a" });
        await GamesEndpointsTests.AssertProblemAsync(tooLong, HttpStatusCode.BadRequest, "invalid_text");
        await AssertStateAsync(context, game.Id, 4.5m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(19)]
    [InlineData(int.MaxValue)]
    public async Task MissingLocalGame_Is404_AndDoesNotImportAnExternalGame(int id)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.PutAsJsonAsync(PathFor(id), new { rating = 4.5m });
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "game_not_found");
        Assert.Equal(0, context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
        Assert.Empty(await ReadReviewsAsync(context));
    }

    [Fact]
    public async Task SuppliedUserIds_CannotReadEditOrDeleteAnotherUsersReview_EvenForAdmin()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync("Admin");
        var game = await OpenGameAsync(owner);
        using var created = await owner.PutAsJsonAsync(PathFor(game.Id), new { rating = 5m, text = "A" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var a = Assert.Single(await ReadReviewsAsync(context));
        var attackPath = PathFor(game.Id) + $"?userId={a.UserId}";
        using var hidden = await other.GetAsync(attackPath);
        using var missingDelete = await other.DeleteAsync(attackPath);
        await GamesEndpointsTests.AssertProblemAsync(hidden, HttpStatusCode.NotFound, "review_not_found");
        await GamesEndpointsTests.AssertProblemAsync(missingDelete, HttpStatusCode.NotFound, "review_not_found");
        using var otherCreate = await other.PutAsJsonAsync(attackPath, new { rating = 1.5m, text = "B", userId = a.UserId, id = a.Id });
        Assert.Equal(HttpStatusCode.Created, otherCreate.StatusCode);
        Assert.NotEqual(a.Id, (await otherCreate.Content.ReadFromJsonAsync<UpsertReviewResponse>())!.Review.Id);
        using var otherEdit = await other.PutAsJsonAsync(attackPath, new { rating = 2.5m, text = "B editada", userId = a.UserId, id = a.Id });
        Assert.Equal(HttpStatusCode.OK, otherEdit.StatusCode);
        Assert.Equal(a.ConcurrencyToken, (await ReadReviewsAsync(context)).Single(review => review.Id == a.Id).ConcurrencyToken);
        Assert.Equal("A", (await owner.GetFromJsonAsync<MyReviewDto>(PathFor(game.Id)))!.Text);
        using var otherDelete = await other.DeleteAsync(attackPath);
        Assert.Equal(HttpStatusCode.OK, otherDelete.StatusCode);
        await AssertStateAsync(context, game.Id, 5m);
        Assert.Equal(a.ConcurrencyToken, Assert.Single(await ReadReviewsAsync(context)).ConcurrencyToken);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task PersistentConcurrencyConflict_Is409(string method)
    {
        using var context = new GamesApiTestContext();
        using var authenticated = await context.AuthenticatedClientAsync();
        var service = new Mock<IReviewService>(MockBehavior.Strict);
        if (method == "PUT") service.Setup(value => value.UpsertAsync(It.IsAny<int>(), 7, 4.5m, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReviewException("concurrent_modification"));
        else service.Setup(value => value.DeleteAsync(It.IsAny<int>(), 7, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReviewException("concurrent_modification"));
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReviewService>();
            services.AddSingleton(service.Object);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        using var request = new HttpRequestMessage(new HttpMethod(method), PathFor(7));
        if (method == "PUT") request.Content = JsonContent.Create(new { rating = 4.5m });
        using var response = await client.SendAsync(request);
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrent_modification");
        service.VerifyAll();
    }

    [Fact]
    public async Task Detail_ReadsSummaryFromDatabase_EvenIfGameServiceReturnsStaleCounters()
    {
        using var context = new GamesApiTestContext();
        using var authenticated = await context.AuthenticatedClientAsync();
        var game = await OpenGameAsync(authenticated);
        var stale = Assert.Single(await context.ReadGamesAsync());
        using var created = await authenticated.PutAsJsonAsync(PathFor(game.Id), new { rating = 4m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var service = new Mock<IGameService>(MockBehavior.Strict);
        service.Setup(value => value.GetOrCreateAsync("gamebrain", 19, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGameService>();
            services.AddSingleton(service.Object);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        using var response = await client.GetAsync("/api/games/19");
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var summary = json.RootElement.GetProperty("ratingSummary");
        Assert.Equal("4.0", summary.GetProperty("average").GetRawText());
        Assert.Equal(1, summary.GetProperty("count").GetInt32());
        Assert.Equal(0, stale.RatingCount);
        Assert.Equal(0m, stale.RatingSum);
        Assert.Equal(1, context.Provider.DetailCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_RefreshOrProviderFailure_PreservesAndReturnsCurrentSummary(bool unavailable)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var game = await OpenGameAsync(client);
        using var created = await client.PutAsJsonAsync(PathFor(game.Id), new { rating = 4.5m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await context.MakeStaleAsync();
        context.Provider.Unavailable = unavailable;
        Assert.Equal(new RatingSummary(4.5m, 1), (await OpenGameAsync(client)).RatingSummary);
        Assert.Equal(2, context.Provider.DetailCalls);
        await AssertStateAsync(context, game.Id, 4.5m);
    }

    internal static string PathFor(int gameId) => $"/api/games/{gameId}/reviews/me";

    internal static async Task<GameDetailsDto> OpenGameAsync(HttpClient client)
        => (await client.GetFromJsonAsync<GameDetailsDto>("/api/games/19"))!;

    internal static async Task<List<Review>> ReadReviewsAsync(GamesApiTestContext context)
    {
        using var scope = context.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Reviews.AsNoTracking().ToListAsync();
    }

    internal static async Task AssertStateAsync(GamesApiTestContext context, int gameId, params decimal[] ratings)
    {
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = await db.Games.AsNoTracking().SingleAsync(value => value.Id == gameId);
        var rows = await db.Reviews.AsNoTracking().Where(review => review.GameId == gameId).ToListAsync();
        Assert.Equal(ratings.Order().ToArray(), rows.Select(review => review.Rating).Order().ToArray());
        Assert.Equal(rows.Count, game.RatingCount);
        Assert.Equal(rows.Sum(review => review.Rating), game.RatingSum);
    }
}
