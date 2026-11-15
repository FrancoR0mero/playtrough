using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class TimelineEndpointsTests
{
    [Theory]
    [InlineData("/api/timeline/me")]
    [InlineData("/api/timeline/me/periods")]
    [InlineData("/api/users/someone/timeline")]
    [InlineData("/api/users/someone/timeline/periods")]
    public async Task AllReadEndpoints_RequireAuthentication(string path)
    {
        using var c = new GamesApiTestContext();
        using var client = c.Factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("month=1")]
    [InlineData("year=1969")]
    [InlineData("year=9999")]
    [InlineData("year=2025&month=0")]
    [InlineData("year=2025&month=13")]
    [InlineData("year=abc")]
    [InlineData("month=1.5")]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("limit=abc")]
    [InlineData("type=removed")]
    [InlineData("type=added,,reviewed")]
    [InlineData("type=")]
    [InlineData("type=added&type=reviewed")]
    public async Task InvalidFilters_ReturnStableProblemCode(string query)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        await Problem(await client.GetAsync("/api/timeline/me?" + query), 400, "invalid_filter");
    }

    [Theory]
    [InlineData("cursor=garbage")]
    [InlineData("cursor=v2.abc")]
    [InlineData("cursor=v1.a%3D")]
    [InlineData("cursor=")]
    [InlineData("cursor=a&cursor=b")]
    public async Task InvalidCursors_ReturnStableProblemCode(string query)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        await Problem(await client.GetAsync("/api/timeline/me?" + query), 400, "invalid_cursor");
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task AnyRole_CanReadOwnTimelineAndPeriods_AndLiteralRoutesDoNotResolveAsUsers(string role)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync(role);
        var game = await ReviewEndpointsTests.OpenGameAsync(client);
        (await client.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        var list = (await client.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!;
        var entry = Assert.Single(list.Items);
        Assert.Equal("added", entry.Type); Assert.Equal(game.Id, entry.Game.GameId);
        Assert.Null(entry.Review); Assert.False(list.HasMore); Assert.Null(list.NextCursor);
        var periods = (await client.GetFromJsonAsync<TimelinePeriodsDto>("/api/timeline/me/periods?type=added"))!;
        Assert.Equal(1, Assert.Single(periods.Years).Count);
        using var literal = await client.GetAsync("/api/users/me/timeline");
        Assert.Equal(HttpStatusCode.NotFound, literal.StatusCode);
        Assert.Equal(entry.Id, Assert.Single((await client.GetFromJsonAsync<TimelinePageDto>("/api/users/GAMES_USER_1/timeline"))!.Items).Id);
    }

    [Fact]
    public async Task HiddenEntriesAndPrivacy_AreEnforcedByBothHttpViews()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        using (var scope = c.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().TimelineEntries.ExecuteUpdateAsync(u => u.SetProperty(e => e.IsHidden, true));
        Assert.True(Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).IsHidden);
        Assert.Empty((await stranger.GetFromJsonAsync<TimelinePageDto>("/api/users/games_user_1/timeline"))!.Items);
        Assert.Empty((await stranger.GetFromJsonAsync<TimelinePeriodsDto>("/api/users/games_user_1/timeline/periods"))!.Years);
        (await owner.PutAsJsonAsync("/api/users/me/privacy", new { visibility = "private" })).EnsureSuccessStatusCode();
        foreach (var suffix in new[] { "", "/periods" })
        {
            using var denied = await stranger.GetAsync("/api/users/games_user_1/timeline" + suffix);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var body = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.Equal("history_restricted", body.RootElement.GetProperty("code").GetString());
            Assert.Equal("private", body.RootElement.GetProperty("restriction").GetString());
            Assert.False(body.RootElement.TryGetProperty("items", out _));
        }
        await Problem(await stranger.GetAsync("/api/users/missing/timeline"), 404, "user_not_found");
        await Problem(await stranger.GetAsync("/api/users/missing/timeline/periods"), 404, "user_not_found");
        await Problem(await owner.GetAsync("/api/timeline/me/periods?type=invalid"), 400, "invalid_filter");
    }

    [Fact]
    public async Task DefaultReadBudget_Is60AndSharedAcrossAllTimelineViews_PerUser()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var owner = await c.AuthenticatedClientAsync();
        using var other = await c.AuthenticatedClientAsync();
        using var anonymous = c.Factory.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/timeline/me")).StatusCode);
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(i % 2 == 0 ? "/api/timeline/me" : "/api/timeline/me/periods")).StatusCode);
        using var rejected = await owner.GetAsync("/api/users/games_user_1/timeline/periods");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/timeline/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/library")).StatusCode);
    }

    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        using (response) await ProfileEndpointsTests.Error(response, status, code);
    }
}
