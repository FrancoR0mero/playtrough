using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class PrivacyEndpointsTests
{
    [Theory]
    [InlineData("public", 0, true)]
    [InlineData("public", 1, true)]
    [InlineData("public", 2, true)]
    [InlineData("private", 0, true)]
    [InlineData("private", 1, true)]
    [InlineData("private", 2, false)]
    public async Task ProfileAndFriendsMatrix_RestrictsExactFields(string visibility, int viewer, bool visible)
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var friend = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        var request = await Send(owner, "games_user_2");
        (await friend.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        await ProfileEndpointsTests.Put(owner, new { bio = "sensitive bio" });
        await Set(owner, visibility);
        var client = new[] { owner, friend, stranger }[viewer];
        using var profile = (await client.GetFromJsonAsync<JsonDocument>("/api/users/GAMES_USER_1"))!;
        var root = profile.RootElement;
        Assert.Equal(!visible, root.GetProperty("isRestricted").GetBoolean());
        Assert.False(root.TryGetProperty("email", out _));
        Assert.False(root.TryGetProperty("id", out _));
        Assert.Equal("games_user_1", root.GetProperty("username").GetString());
        Assert.Equal(viewer == 1 ? "friends" : "none", root.GetProperty("relationship").GetProperty("status").GetString());
        if (visible)
        {
            Assert.Equal("sensitive bio", root.GetProperty("bio").GetString());
            Assert.Equal(1, root.GetProperty("friendsCount").GetInt32());
            Assert.Equal(JsonValueKind.String, root.GetProperty("createdAt").ValueKind);
        }
        else
        {
            Assert.Equal(new[] { "avatarUrl", "isRestricted", "relationship", "restriction", "username" },
                root.EnumerateObject().Select(p => p.Name).Order());
            Assert.Equal("private", root.GetProperty("restriction").GetString());
        }
        using var list = await client.GetAsync("/api/users/GAMES_USER_1/friends?page=1&pageSize=1");
        if (visible)
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var page = (await list.Content.ReadFromJsonAsync<FriendsPage<FriendDto>>())!;
            Assert.Equal("games_user_2", Assert.Single(page.Items).User.Username);
            Assert.Equal(1, page.PageSize);
            Assert.False(page.HasMore);
        }
        else await ProfileEndpointsTests.Error(list, 403, "profile_restricted");
        Assert.Equal("sensitive bio", (await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.Bio);
    }

    [Fact]
    public async Task FriendsVisibility_GrantAndRevokeAreImmediate_AndPrivateStillReceivesRequests()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        await Set(owner, "private");
        var request = await Send(viewer, "games_user_1");
        var restricted = (await viewer.GetFromJsonAsync<ProfileDto>("/api/users/games_user_1"))!;
        Assert.True(restricted.IsRestricted);
        Assert.Null(restricted.Bio);
        Assert.Null(restricted.CreatedAt);
        Assert.Null(restricted.FriendsCount);
        Assert.Equal(new RelationshipDto("request_sent", request.Id), restricted.Relationship);
        Assert.Equal(1, (await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.PendingReceivedCount);
        var found = (await viewer.GetFromJsonAsync<UserSearchResponse>("/api/users/search?q=games_user_1"))!;
        Assert.Equal("games_user_1", Assert.Single(found.Items).Username);
        await Set(owner, "private");
        Assert.True((await viewer.GetFromJsonAsync<ProfileDto>("/api/users/games_user_1"))!.IsRestricted);
        (await owner.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        Assert.False((await viewer.GetFromJsonAsync<ProfileDto>("/api/users/games_user_1"))!.IsRestricted);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/users/games_user_1/friends")).StatusCode);
        (await viewer.DeleteAsync("/api/friends/games_user_1")).EnsureSuccessStatusCode();
        Assert.True((await viewer.GetFromJsonAsync<ProfileDto>("/api/users/games_user_1"))!.IsRestricted);
        await ProfileEndpointsTests.Error(await viewer.GetAsync("/api/users/games_user_1/friends"), 403, "profile_restricted");
        await Set(owner, "public");
        Assert.False((await viewer.GetFromJsonAsync<ProfileDto>("/api/users/games_user_1"))!.IsRestricted);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"visibility\":null}")]
    [InlineData("{\"visibility\":0}")]
    [InlineData("{\"visibility\":\"Public\"}")]
    [InlineData("{\"visibility\":\"friends\"}")]
    [InlineData("{\"visibility\":[]}")]
    [InlineData("{\"profileVisibility\":\"public\",\"historyVisibility\":\"private\"}")]
    public async Task InvalidVisibility_Is400AndDoesNotPartiallyUpdate(string json)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        await ProfileEndpointsTests.Error(await client.PutAsync("/api/users/me/privacy", new StringContent(json, Encoding.UTF8, "application/json")),
            400, "invalid_visibility");
        Assert.Equal(new PrivacyDto("public"), await client.GetFromJsonAsync<PrivacyDto>("/api/users/me/privacy"));
    }

    [Theory]
    [InlineData("GET", "/api/users/me/privacy")]
    [InlineData("PUT", "/api/users/me/privacy")]
    [InlineData("GET", "/api/users/someone/friends")]
    public async Task NoToken_401(string method, string path)
    {
        using var c = new GamesApiTestContext();
        using var client = c.Factory.CreateClient();
        using var message = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT") message.Content = JsonContent.Create(new { visibility = "private" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(message)).StatusCode);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task AnyRole_CanOnlyChangeOwnSettings(string role)
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync(role);
        using var b = await c.AuthenticatedClientAsync();
        var other = (await b.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!;
        using var response = await a.PutAsJsonAsync("/api/users/me/privacy", new
        {
            visibility = "private", userId = other.Id,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new PrivacyDto("private"), await a.GetFromJsonAsync<PrivacyDto>("/api/users/me/privacy"));
        Assert.Equal(new PrivacyDto("public"), await b.GetFromJsonAsync<PrivacyDto>("/api/users/me/privacy"));
    }

    [Fact]
    public async Task ProfileWriteBudget_IsSharedWithPrivacy_AndReadsRemainAvailable()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var client = await c.AuthenticatedClientAsync();
        for (var i = 0; i < 9; i++) await ProfileEndpointsTests.Put(client, new { bio = i.ToString() });
        await Set(client, "private");
        using var response = await client.PutAsJsonAsync("/api/users/me/privacy", new { visibility = "public" });
        await ProfileEndpointsTests.Error(response, 429, "rate_limited");
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        await ProfileEndpointsTests.Error(await client.PutAsJsonAsync("/api/users/me", new { bio = "blocked" }), 429, "rate_limited");
        Assert.Equal(new PrivacyDto("private"), await client.GetFromJsonAsync<PrivacyDto>("/api/users/me/privacy"));
    }

    [Fact]
    public async Task MissingAndUnconfirmedFriendsLists_Are404_OwnerStillAllowed()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var other = await c.AuthenticatedClientAsync();
        await Set(owner, "private");
        using (var scope = c.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.UserName == "games_user_1")
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.EmailConfirmed, false));
        await ProfileEndpointsTests.Error(await other.GetAsync("/api/users/games_user_1/friends"), 404, "user_not_found");
        await ProfileEndpointsTests.Error(await other.GetAsync("/api/users/missing/friends"), 404, "user_not_found");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/users/games_user_1/friends")).StatusCode);
        await ProfileEndpointsTests.Error(await owner.GetAsync("/api/users/games_user_1/friends?page=abc"), 400, "invalid_query");
        await ProfileEndpointsTests.Error(await owner.GetAsync("/api/users/games_user_1/friends?pageSize=51"), 400, "invalid_query");
    }

    [Fact]
    public async Task PrivateAccount_HidesReviewsFromStrangers_AndPreservesOwnLibrary()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PutAsJsonAsync($"/api/games/{game.Id}/reviews/me", new { rating = 4.5, text = "still visible review" })).EnsureSuccessStatusCode();
        await Set(owner, "private");
        using var reviews = await viewer.GetAsync($"/api/games/{game.Id}/reviews");
        Assert.Equal(HttpStatusCode.OK, reviews.StatusCode);
        Assert.DoesNotContain("still visible review", await reviews.Content.ReadAsStringAsync());
        Assert.Contains("still visible review", await owner.GetStringAsync($"/api/games/{game.Id}/reviews"));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/library")).StatusCode);
    }

    internal static async Task Set(HttpClient client, string visibility)
    {
        using var response = await client.PutAsJsonAsync("/api/users/me/privacy", new { visibility });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new PrivacyDto(visibility), await response.Content.ReadFromJsonAsync<PrivacyDto>());
    }
    private static async Task<SentFriendRequestDto> Send(HttpClient client, string username)
    {
        using var response = await client.PostAsJsonAsync("/api/friends/requests", new { username });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
    }
}
