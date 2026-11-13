using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Library;
using Xunit;

namespace Playtrough.Tests;

public class UserLibraryPrivacyTests
{
    private const string Path = "/api/users/GAMES_USER_1/library";

    [Theory]
    [InlineData("public", 0, true)]
    [InlineData("public", 1, true)]
    [InlineData("public", 2, true)]
    [InlineData("friends", 0, true)]
    [InlineData("friends", 1, true)]
    [InlineData("friends", 2, false)]
    [InlineData("private", 0, true)]
    [InlineData("private", 1, false)]
    [InlineData("private", 2, false)]
    public async Task HistoryMatrix_IsIndependentOfProfile_AndDoesNotLeakCounts(string visibility, int viewer, bool allowed)
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var friend = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        using var sent = await owner.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await friend.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        await PrivacyEndpointsTests.Set(owner, "private", visibility);
        var client = new[] { owner, friend, stranger }[viewer];
        using var response = await client.GetAsync(Path);
        if (allowed)
        {
            response.EnsureSuccessStatusCode();
            var page = (await response.Content.ReadFromJsonAsync<LibraryListResponseDto>())!;
            Assert.Equal(game.Id, Assert.Single(page.Items).GameId);
            Assert.Equal("playing", page.Items[0].Status);
            Assert.Equal(1, page.Counts.Playing);
            Assert.Equal(1, page.Counts.Total);
            Assert.DoesNotContain("userId", await response.Content.ReadAsStringAsync());
        }
        else
        {
            await ProfileEndpointsTests.Error(response, 403, "history_restricted");
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("items", json);
            Assert.DoesNotContain("counts", json);
        }
    }

    [Fact]
    public async Task PreferencesAndFriendshipChanges_ApplyImmediately_AndOwnLibraryIsUnchanged()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        await PrivacyEndpointsTests.Set(owner, "public", "friends");
        await ProfileEndpointsTests.Error(await viewer.GetAsync(Path), 403, "history_restricted");
        using var sent = await viewer.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_1" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await owner.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Path)).StatusCode);
        (await viewer.DeleteAsync("/api/friends/games_user_1")).EnsureSuccessStatusCode();
        await ProfileEndpointsTests.Error(await viewer.GetAsync(Path), 403, "history_restricted");
        await PrivacyEndpointsTests.Set(owner, "private", "public");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Path)).StatusCode);
        await PrivacyEndpointsTests.Set(owner, "public", "private");
        await ProfileEndpointsTests.Error(await viewer.GetAsync(Path), 403, "history_restricted");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/library")).StatusCode);
    }

    [Fact]
    public async Task FilteringAndPagination_OnlyReadTheOwnersGames()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "completed" })).EnsureSuccessStatusCode();
        (await viewer.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "backlog" })).EnsureSuccessStatusCode();
        await PrivacyEndpointsTests.Set(owner, "public", "public");
        var page = (await viewer.GetFromJsonAsync<LibraryListResponseDto>(Path + "?status=completed&pageSize=1&sort=name"))!;
        Assert.Equal("completed", Assert.Single(page.Items).Status);
        Assert.Equal(1, page.PageSize);
        Assert.False(page.HasMore);
        Assert.Empty((await viewer.GetFromJsonAsync<LibraryListResponseDto>(Path + "?status=backlog"))!.Items);
        Assert.Empty((await viewer.GetFromJsonAsync<LibraryListResponseDto>(Path + "?q=nonexistentgame"))!.Items);
        Assert.Empty((await viewer.GetFromJsonAsync<LibraryListResponseDto>(Path + "?page=2&pageSize=1"))!.Items);
        Assert.Equal("backlog", Assert.Single((await viewer.GetFromJsonAsync<LibraryListResponseDto>("/api/library"))!.Items).Status);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    [InlineData("pageSize=41")]
    [InlineData("status=unknown")]
    [InlineData("sort=unknown")]
    public async Task InvalidQueries_Return400(string query)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        await ProfileEndpointsTests.Error(await client.GetAsync(Path + "?" + query), 400, "invalid_query");
    }

    [Fact]
    public async Task MissingUnconfirmedAndAnonymous_AreProtected()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        using var anonymous = c.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path)).StatusCode);
        await ProfileEndpointsTests.Error(await viewer.GetAsync("/api/users/missing/library"), 404, "user_not_found");
        using (var scope = c.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.UserName == "games_user_1")
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.EmailConfirmed, false));
        await ProfileEndpointsTests.Error(await viewer.GetAsync(Path), 404, "user_not_found");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(Path)).StatusCode);
    }
}
