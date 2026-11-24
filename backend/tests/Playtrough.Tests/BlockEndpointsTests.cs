using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Blocks;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class BlockEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Block_CancelsRequestsInEitherDirection_AndRepeatedWritesAreIdempotent(bool reverse)
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        var sender = reverse ? b : a;
        using var sent = await sender.PostAsJsonAsync("/api/friends/requests", new { username = reverse ? "games_user_1" : "games_user_2" });
        sent.EnsureSuccessStatusCode();
        await Block(a, "GAMES_USER_2");
        var first = Assert.Single((await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
        await Block(a, "games_user_2");
        Assert.Equal(first, Assert.Single((await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items));
        Assert.Equal("games_user_2", first.User.Username);
        Assert.Empty((await b.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
        Assert.Equal(0, (await a.GetFromJsonAsync<FriendsSummaryDto>("/api/friends/summary"))!.PendingSentCount);
        Assert.Equal(0, (await b.GetFromJsonAsync<FriendsSummaryDto>("/api/friends/summary"))!.PendingReceivedCount);
        using var scope = c.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(FriendRequestStatus.Canceled, (await db.FriendRequests.SingleAsync()).Status);
        Assert.Single(await db.UserBlocks.ToListAsync());
    }

    [Fact]
    public async Task BlockingFriends_RemovesFriendship_AndUnblockingRestoresNothing()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        using var sent = await a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await b.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        await Block(a, "games_user_2");
        foreach (var client in new[] { a, b })
            Assert.Equal(0, (await client.GetFromJsonAsync<FriendsSummaryDto>("/api/friends/summary"))!.FriendsCount);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/blocks/games_user_2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/blocks/games_user_2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/blocks/missing")).StatusCode);
        using var scope = c.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Friendships.ToListAsync());
        Assert.Empty(await db.UserBlocks.ToListAsync());
        Assert.Equal(FriendRequestStatus.Accepted, (await db.FriendRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task BlocksAreDirectional_AndCannotBeDeletedByTheOtherAccount()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        await Block(a, "games_user_2");
        (await b.DeleteAsync("/api/blocks/games_user_1")).EnsureSuccessStatusCode();
        Assert.Single((await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
        await Block(b, "games_user_1");
        (await a.DeleteAsync("/api/blocks/games_user_2")).EnsureSuccessStatusCode();
        Assert.Empty((await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
        Assert.Single((await b.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"username\":null}")]
    [InlineData("{\"username\":123}")]
    [InlineData("{\"username\":\" \"}")]
    [InlineData("{broken")]
    public async Task InvalidBodies_Return400(string json)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        await ProfileEndpointsTests.Error(await client.PostAsync("/api/blocks", new StringContent(json, Encoding.UTF8, "application/json")), 400, "invalid_query");
    }

    [Fact]
    public async Task OwnAndMissingAndUnconfirmedTargets_AreRejected_ExistingBlocksRemainManageable()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        await ProfileEndpointsTests.Error(await a.PostAsJsonAsync("/api/blocks", new { username = "games_user_1" }), 400, "cannot_block_self");
        await ProfileEndpointsTests.Error(await a.PostAsJsonAsync("/api/blocks", new { username = "missing" }), 404, "user_not_found");
        await Block(a, "games_user_2");
        using (var scope = c.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.UserName == "games_user_2")
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.EmailConfirmed, false));
        await Block(a, "games_user_2");
        Assert.Single((await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
        (await a.DeleteAsync("/api/blocks/games_user_2")).EnsureSuccessStatusCode();
        await ProfileEndpointsTests.Error(await a.PostAsJsonAsync("/api/blocks", new { username = "games_user_2" }), 404, "user_not_found");
    }

    [Fact]
    public async Task List_IsPaginatedAndPrivate_AndContainsNoInternalIdsOrEmail()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        using var d = await c.AuthenticatedClientAsync();
        await Block(a, "games_user_3");
        await Block(a, "games_user_2");
        using var response = await a.GetAsync("/api/blocks?pageSize=1");
        var first = (await response.Content.ReadFromJsonAsync<BlockedUsersPage>())!;
        Assert.True(first.HasMore);
        Assert.Equal("games_user_2", Assert.Single(first.Items).User.Username);
        var second = (await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks?pageSize=1&page=2"))!;
        Assert.False(second.HasMore);
        Assert.Equal("games_user_3", Assert.Single(second.Items).User.Username);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", json);
        Assert.DoesNotContain("blockerId", json);
        Assert.DoesNotContain("blockedId", json);
        foreach (var query in new[] { "page=0", "page=abc", "pageSize=51", "page=2147483647&pageSize=50" })
            await ProfileEndpointsTests.Error(await a.GetAsync("/api/blocks?" + query), 400, "invalid_query");
    }

    [Theory]
    [InlineData("GET", "/api/blocks")]
    [InlineData("POST", "/api/blocks")]
    [InlineData("DELETE", "/api/blocks/anyone")]
    public async Task EndpointsRequireAuthentication(string method, string path)
    {
        using var c = new GamesApiTestContext();
        using var client = c.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new { username = "anyone" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task WritesShareFriendsRateBudget()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        for (var i = 0; i < 30; i++) await Block(a, "games_user_2");
        using var response = await a.DeleteAsync("/api/blocks/games_user_2");
        await ProfileEndpointsTests.Error(response, 429, "rate_limited");
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        await ProfileEndpointsTests.Error(await a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" }), 429, "rate_limited");
        Assert.Single((await a.GetFromJsonAsync<BlockedUsersPage>("/api/blocks"))!.Items);
    }

    [Fact]
    public void Migration_HasDirectionalKeyReverseIndexChecksAndNoPendingModelChanges()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("AddUserAvatar", "AddUserBlocks");
        Assert.Contains("CREATE TABLE `UserBlocks`", sql);
        Assert.Contains("PRIMARY KEY (`BlockerId`, `BlockedId`)", sql);
        Assert.Contains("CHECK (`BlockerId` <> `BlockedId`)", sql);
        Assert.Contains("IX_UserBlocks_BlockedId_BlockerId", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    internal static async Task Block(HttpClient client, string username)
    {
        using var response = await client.PostAsJsonAsync("/api/blocks", new { username });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
