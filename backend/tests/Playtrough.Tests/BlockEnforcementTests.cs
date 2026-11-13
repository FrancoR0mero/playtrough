using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class BlockEnforcementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockingEitherDirection_HidesProfilesLibrariesFriendsAndSearch(bool reverse)
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        await PrivacyEndpointsTests.Set(a, "public", "public");
        await PrivacyEndpointsTests.Set(b, "public", "public");
        await BlockEndpointsTests.Block(reverse ? b : a, reverse ? "games_user_1" : "games_user_2");
        foreach (var (client, target) in new[] { (a, "games_user_2"), (b, "games_user_1") })
        {
            foreach (var suffix in new[] { "", "/library", "/friends" })
                await ProfileEndpointsTests.Error(await client.GetAsync($"/api/users/{target}{suffix}"), 404, "user_not_found");
            Assert.Empty((await client.GetFromJsonAsync<UserSearchResponse>($"/api/users/search?q={target}"))!.Items);
            await ProfileEndpointsTests.Error(await client.PostAsJsonAsync("/api/friends/requests", new { username = target }), 404, "user_not_found");
        }
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/users/games_user_1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/library")).StatusCode);
    }

    [Fact]
    public async Task PrivacyService_BlocksBothScopes_AndUnblockOnlyRemovesOwnDirection()
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb();
        var blocks = new BlockService(db, new UpperInvariantLookupNormalizer(), c.Clock);
        var privacy = new PrivacyService(db);
        await blocks.BlockAsync(1, FriendTestContext.Name(2));
        foreach (var scope in new[] { PrivacyScope.Profile, PrivacyScope.History })
        {
            Assert.False(await privacy.CanViewAsync(1, 2, scope));
            Assert.False(await privacy.CanViewAsync(2, 1, scope));
            Assert.True(await privacy.CanViewAsync(1, 1, scope));
        }
        await blocks.BlockAsync(2, FriendTestContext.Name(1));
        await blocks.UnblockAsync(1, FriendTestContext.Name(2));
        Assert.True(await privacy.IsBlockedAsync(1, 2));
        await blocks.UnblockAsync(2, FriendTestContext.Name(1));
        Assert.False(await privacy.IsBlockedAsync(1, 2));
        Assert.True(await privacy.CanViewAsync(1, 2, PrivacyScope.Profile));
    }

    [Fact]
    public async Task CanceledAndHistoricalAcceptedRequests_CannotBeUsedWhileBlocked()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        using var sent = await a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        await BlockEndpointsTests.Block(a, "games_user_2");
        await ProfileEndpointsTests.Error(await b.PostAsync($"/api/friends/requests/{request.Id}/accept", null), 404, "request_not_found");
        (await a.DeleteAsync("/api/blocks/games_user_2")).EnsureSuccessStatusCode();
        using var second = await a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var next = (await second.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await b.PostAsync($"/api/friends/requests/{next.Id}/accept", null)).EnsureSuccessStatusCode();
        await BlockEndpointsTests.Block(b, "games_user_1");
        await ProfileEndpointsTests.Error(await b.PostAsync($"/api/friends/requests/{next.Id}/accept", null), 404, "request_not_found");
        Assert.Equal(0, (await a.GetFromJsonAsync<FriendsSummaryDto>("/api/friends/summary"))!.FriendsCount);
    }

    [Fact]
    public async Task ThirdPartyFriendsList_DoesNotExposeBlockedAccounts()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        using var third = await c.AuthenticatedClientAsync();
        using var sent = await third.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await b.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        await BlockEndpointsTests.Block(a, "games_user_2");
        Assert.Empty((await a.GetFromJsonAsync<FriendsPage<FriendDto>>("/api/users/games_user_3/friends"))!.Items);
        Assert.Single((await third.GetFromJsonAsync<FriendsPage<FriendDto>>("/api/friends"))!.Items);
    }

    [Fact]
    public async Task PublishedReviewsRemainVisible()
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(a);
        (await a.PutAsJsonAsync($"/api/games/{game.Id}/reviews/me", new { rating = 4, text = "public review" })).EnsureSuccessStatusCode();
        await BlockEndpointsTests.Block(b, "games_user_1");
        using var response = await b.GetAsync($"/api/games/{game.Id}/reviews");
        response.EnsureSuccessStatusCode();
        Assert.Contains("public review", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedBlockInsert_RollsBackRelationshipAndRequestChanges(bool accepted)
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        if (accepted) await c.Run(s => s.AcceptAsync(2, request.Id));
        await using var db = c.CreateDb();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailBlock BEFORE INSERT ON UserBlocks BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        var blocks = new BlockService(db, new UpperInvariantLookupNormalizer(), c.Clock);
        await Assert.ThrowsAsync<DbUpdateException>(() => blocks.BlockAsync(1, FriendTestContext.Name(2)));
        Assert.Empty(await db.UserBlocks.ToListAsync());
        Assert.Equal(accepted, await db.Friendships.AnyAsync());
        Assert.Equal(accepted ? FriendRequestStatus.Accepted : FriendRequestStatus.Pending,
            (await db.FriendRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ConcurrentAcceptanceAndBlock_AlwaysEndWithBlockAndNoFriendship()
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        c.Events.OnPublish = null;
        c.SynchronizeInitialReads();
        async Task Block()
        {
            await using var db = c.CreateDb();
            await new BlockService(db, new UpperInvariantLookupNormalizer(), c.Clock).BlockAsync(1, FriendTestContext.Name(2));
        }
        async Task Accept()
        {
            try { await c.Run(s => s.AcceptAsync(2, request.Id)); }
            catch (FriendException error) { Assert.Equal("request_not_found", error.Code); }
        }
        await Task.WhenAll(Block(), Accept());
        await using var check = c.CreateDb(false);
        Assert.Single(await check.UserBlocks.ToListAsync());
        Assert.Empty(await check.Friendships.ToListAsync());
        Assert.False(await check.FriendRequests.AnyAsync(r => r.Status == FriendRequestStatus.Pending));
    }

    [Fact]
    public async Task ConcurrentDuplicateBlocks_KeepOneRow()
    {
        using var c = new FriendTestContext();
        c.Events.OnPublish = null;
        c.SynchronizeInitialReads();
        async Task Block()
        {
            await using var db = c.CreateDb();
            await new BlockService(db, new UpperInvariantLookupNormalizer(), c.Clock).BlockAsync(1, FriendTestContext.Name(2));
        }
        await Task.WhenAll(Block(), Block());
        await using var check = c.CreateDb(false);
        Assert.Single(await check.UserBlocks.ToListAsync());
    }
}
