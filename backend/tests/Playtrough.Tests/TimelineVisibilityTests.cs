using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class TimelineVisibilityTests
{
    [Fact]
    public async Task HideAndShow_AreIdempotent_AndUpdateBothPublicViewsWhileOwnerKeepsHistory()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        var entry = await CreateEntry(owner);
        var path = PathFor(entry.Id);
        var publicPath = "/api/users/games_user_1/timeline";
        Assert.Single((await viewer.GetFromJsonAsync<TimelinePageDto>(publicPath))!.Items);
        var hidden = await Set(owner, path, true);
        Assert.True(hidden.IsHidden);
        Assert.Equal(entry.Id, hidden.Id); Assert.Equal(entry.RecordedAt, hidden.RecordedAt); Assert.Equal(entry.OccurredOn, hidden.OccurredOn);
        Assert.Equal(hidden, await Set(owner, path, true));
        Assert.True(Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).IsHidden);
        Assert.Equal(1, (await owner.GetFromJsonAsync<TimelinePeriodsDto>("/api/timeline/me/periods"))!.Years.Sum(y => y.Count));
        Assert.Empty((await viewer.GetFromJsonAsync<TimelinePageDto>(publicPath))!.Items);
        Assert.Empty((await viewer.GetFromJsonAsync<TimelinePeriodsDto>(publicPath + "/periods"))!.Years);
        var shown = await Set(owner, path, false);
        Assert.False(shown.IsHidden); Assert.Equal(entry.Id, shown.Id);
        Assert.Equal(shown, await Set(owner, path, false));
        Assert.Single((await viewer.GetFromJsonAsync<TimelinePageDto>(publicPath))!.Items);
        Assert.Equal(1, (await viewer.GetFromJsonAsync<TimelinePeriodsDto>(publicPath + "/periods"))!.Years.Sum(y => y.Count));
        using var scope = c.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.TimelineEntries.AsNoTracking().ToListAsync());
        Assert.Single(await db.LibraryEntries.AsNoTracking().ToListAsync());
        Assert.Single(await db.LibraryEvents.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task OtherUsers_CannotWriteEvenWithAnOwnerIdInTheBody_AndReceiveTheSame404AsMissing(string role)
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var other = await c.AuthenticatedClientAsync(role);
        var entry = await CreateEntry(owner);
        foreach (var id in new[] { entry.Id, long.MaxValue, -1L, 0L })
        {
            using var response = await other.PutAsJsonAsync(PathFor(id), new { hidden = true, userId = 1 });
            await ProfileEndpointsTests.Error(response, 404, "entry_not_found");
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("isHidden", json); Assert.DoesNotContain("userId", json);
        }
        Assert.False(Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).IsHidden);
    }

    [Fact]
    public async Task FriendshipGrantsReadingButNeverPermissionToChangeAnotherUsersEntry()
    {
        using var c = new TimelineReadTestContext();
        var entry = await c.AddAsync();
        await using var db = c.Store.CreateDb();
        db.Friendships.Add(new Friendship { UserLowId = c.Owner, UserHighId = c.Friend, CreatedAt = c.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        Assert.Single((await c.ListAsync(viewer: c.Friend, username: "review-1")).Items);
        await TimelineReadTests.Error("entry_not_found", () => c.Service(db).SetVisibilityAsync(c.Friend, entry.Id,
            new() { Hidden = JsonSerializer.SerializeToElement(true) }));
        Assert.False((await db.TimelineEntries.AsNoTracking().SingleAsync()).IsHidden);
    }

    [Fact]
    public async Task OwnerCanChangeAnOldEntryOutsideEveryInitiallyLoadedPage()
    {
        using var c = new TimelineReadTestContext();
        var old = await c.AddAsync("2000-01-01");
        for (var i = 0; i < 55; i++) await c.AddAsync();
        Assert.DoesNotContain((await c.ListAsync(new() { Limit = 50 })).Items, e => e.Id == old.Id);
        await using var db = c.Store.CreateDb();
        var updated = await c.Service(db).SetVisibilityAsync(c.Owner, old.Id,
            new() { Hidden = JsonSerializer.SerializeToElement(true) });
        Assert.Equal(old.Id, updated.Id);
        Assert.True(updated.IsHidden);
        Assert.Equal(old.RecordedAt, updated.RecordedAt);
        Assert.Equal(old.OccurredOn, updated.OccurredOn);
    }

    [Fact]
    public async Task ReviewEntryVisibility_DoesNotModifyOrHideTheReviewItself()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PutAsJsonAsync($"/api/games/{game.Id}/reviews/me", new { rating = 4, text = "unchanged review" })).EnsureSuccessStatusCode();
        var entry = Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items);
        var hidden = await Set(owner, PathFor(entry.Id), true);
        Assert.Equal(entry.Review, hidden.Review);
        Assert.Equal(entry.RecordedAt, hidden.RecordedAt);
        Assert.Empty((await viewer.GetFromJsonAsync<TimelinePageDto>("/api/users/games_user_1/timeline"))!.Items);
        Assert.Contains("unchanged review", await viewer.GetStringAsync($"/api/games/{game.Id}/reviews"));
        using var scope = c.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var review = await db.Reviews.AsNoTracking().SingleAsync();
        Assert.Equal(4, review.Rating); Assert.Equal("unchanged review", review.Text); Assert.Null(review.EditedAt);
        Assert.Equal(entry.Review!.Id, review.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"hidden\":null}")]
    [InlineData("{\"hidden\":0}")]
    [InlineData("{\"hidden\":\"true\"}")]
    [InlineData("{\"hidden\":[]}")]
    public async Task InvalidBodies_Are400AndCannotChangeVisibility(string json)
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        var entry = await CreateEntry(owner);
        using var response = await owner.PutAsync(PathFor(entry.Id), new StringContent(json, Encoding.UTF8, "application/json"));
        await ProfileEndpointsTests.Error(response, 400, "invalid_body");
        Assert.False(Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).IsHidden);
    }

    [Fact]
    public async Task DefaultWriteBudget_Allows30ThenRejects_AndDoesNotConsumeTheReadOrAnotherUsersBudget()
    {
        using var c = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var owner = await c.AuthenticatedClientAsync();
        using var other = await c.AuthenticatedClientAsync();
        using var anonymous = c.Factory.CreateClient();
        using var invalid = c.Factory.CreateClient();
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        var entry = await CreateEntry(owner);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(PathFor(entry.Id), new { hidden = true })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.PutAsJsonAsync(PathFor(entry.Id), new { hidden = true })).StatusCode);
        }
        for (var i = 0; i < 30; i++) Assert.True((await Set(owner, PathFor(entry.Id), true)).IsHidden);
        using var rejected = await owner.PutAsJsonAsync(PathFor(entry.Id), new { hidden = false });
        await ProfileEndpointsTests.Error(rejected, 429, "rate_limited");
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).IsHidden);
        var otherEntry = await CreateEntry(other);
        Assert.True((await Set(other, PathFor(otherEntry.Id), true)).IsHidden);
    }

    [Fact]
    public async Task RenewedTokenAndChangedIp_KeepTheSameConfiguredWriteBudget()
    {
        using var c = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var original = await c.AuthenticatedClientAsync();
        var entry = await CreateEntry(original);
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(s => s.TimelineWrite = new(2, TimeSpan.FromMinutes(1)))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        await Set(client, PathFor(entry.Id), true);
        await Set(client, PathFor(entry.Id), false);
        using var renewed = await c.LoginClientAsync();
        client.DefaultRequestHeaders.Authorization = renewed.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "203.0.113.99");
        using var rejected = await client.PutAsJsonAsync(PathFor(entry.Id), new { hidden = true });
        await ProfileEndpointsTests.Error(rejected, 429, "rate_limited");
    }

    [Fact]
    public async Task Swagger_DocumentsBooleanBodyAndOwnerOnlyErrors()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var root = doc.RootElement;
        var put = root.GetProperty("paths").GetProperty("/api/timeline/entries/{id}/visibility").GetProperty("put");
        Assert.True(put.GetProperty("requestBody").GetProperty("required").GetBoolean());
        Assert.Contains("timeline-write", put.GetProperty("description").GetString());
        Assert.Contains("invalid_body", put.GetProperty("responses").GetProperty("400").GetProperty("description").GetString());
        Assert.Contains("entry_not_found", put.GetProperty("responses").GetProperty("404").GetProperty("description").GetString());
        Assert.True(put.GetProperty("responses").GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("UpdateTimelineVisibilityRequest");
        Assert.Equal("hidden", Assert.Single(schema.GetProperty("required").EnumerateArray()).GetString());
        Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("hidden").GetProperty("type").GetString());
    }

    [Fact]
    public void Startup_RejectsInvalidWriteLimits()
    {
        using var c = new GamesApiTestContext();
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(s => s.TimelineWrite = new(0, TimeSpan.Zero))));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private static string PathFor(long id) => $"/api/timeline/entries/{id}/visibility";
    private static async Task<TimelineEntryDto> CreateEntry(HttpClient client)
    {
        var game = await ReviewEndpointsTests.OpenGameAsync(client);
        (await client.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        return Assert.Single((await client.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items);
    }
    private static async Task<TimelineEntryDto> Set(HttpClient client, string path, bool hidden)
    {
        using var response = await client.PutAsJsonAsync(path, new { hidden });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TimelineEntryDto>())!;
    }
}
