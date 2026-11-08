using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class FriendsEndpointsTests
{
    [Theory]
    [InlineData("POST", "/api/friends/requests")]
    [InlineData("GET", "/api/friends/requests/received")]
    [InlineData("GET", "/api/friends/requests/sent")]
    [InlineData("POST", "/api/friends/requests/1/accept")]
    [InlineData("POST", "/api/friends/requests/1/reject")]
    [InlineData("DELETE", "/api/friends/requests/1")]
    [InlineData("GET", "/api/friends")]
    [InlineData("GET", "/api/friends/summary")]
    [InlineData("DELETE", "/api/friends/someone")]
    public async Task AllEndpoints_RequireAuthentication(string method, string path)
    {
        using var context = new GamesApiTestContext();
        using var client = context.Factory.CreateClient();
        using var message = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && path.EndsWith("requests")) message.Content = JsonContent.Create(new { username = "someone" });
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task FullFlow_ProfilesCountersAndRequestsAlwaysUseTokenOwner(string role)
    {
        using var c = new GamesApiTestContext();
        using var a = await c.AuthenticatedClientAsync(role);
        using var b = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        await Relation(a, "games_user_2", "none");
        using var response = await a.PostAsJsonAsync("/api/friends/requests", new
        {
            username = "GAMES_USER_2", senderId = 999, userId = 999,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var raw = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(new[] { "avatarUrl", "username" }, raw!.RootElement.GetProperty("user").EnumerateObject().Select(p => p.Name).Order());
        var request = raw.RootElement.Deserialize<SentFriendRequestDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("pending", request.Status);
        await Relation(a, "games_user_2", "request_sent", request.Id);
        await Relation(b, "games_user_1", "request_received", request.Id);
        var own = (await b.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!;
        Assert.Equal(1, own.PendingReceivedCount);
        var sent = (await a.GetFromJsonAsync<FriendsPage<FriendRequestDto>>("/api/friends/requests/sent"))!;
        Assert.Equal(request.Id, Assert.Single(sent.Items).Id);
        Assert.Equal("games_user_2", sent.Items[0].User.Username);
        var received = (await b.GetFromJsonAsync<FriendsPage<FriendRequestDto>>("/api/friends/requests/received"))!;
        Assert.Equal("games_user_1", Assert.Single(received.Items).User.Username);
        foreach (var suffix in new[] { "sent", "received" })
            Assert.Empty((await stranger.GetFromJsonAsync<FriendsPage<FriendRequestDto>>($"/api/friends/requests/{suffix}?userId={own.Id}"))!.Items);
        await ProfileEndpointsTests.Error(await stranger.PostAsync($"/api/friends/requests/{request.Id}/accept", null), 404, "request_not_found");
        await ProfileEndpointsTests.Error(await a.PostAsync($"/api/friends/requests/{request.Id}/accept", null), 404, "request_not_found");
        var reverse = await ProfileEndpointsTests.Error(await b.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_1" }), 409, "request_already_received");
        Assert.Equal(request.Id, reverse.GetProperty("requestId").GetInt32());
        var acceptedResponse = await b.PostAsync($"/api/friends/requests/{request.Id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        var accepted = (await acceptedResponse.Content.ReadFromJsonAsync<AcceptedFriendDto>())!;
        Assert.Equal("games_user_1", accepted.Friend.Username);
        var repeated = await b.PostAsync($"/api/friends/requests/{request.Id}/accept", null);
        Assert.Equal(accepted, await repeated.Content.ReadFromJsonAsync<AcceptedFriendDto>());
        await Relation(a, "games_user_2", "friends");
        var profile = (await b.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!;
        Assert.Equal(1, profile.FriendsCount);
        Assert.Equal(0, profile.PendingReceivedCount);
        Assert.Equal(new FriendsSummaryDto(1, 0, 0), await a.GetFromJsonAsync<FriendsSummaryDto>("/api/friends/summary"));
        var friends = (await a.GetFromJsonAsync<FriendsPage<FriendDto>>("/api/friends?q=GAMES_USER_2"))!;
        Assert.Equal(accepted.Since, Assert.Single(friends.Items).Since);
        Assert.Empty((await stranger.GetFromJsonAsync<FriendsPage<FriendDto>>($"/api/friends?userId={own.Id}"))!.Items);
        await ProfileEndpointsTests.Error(await a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" }), 409, "already_friends");
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/friends/games_user_2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/friends/games_user_2")).StatusCode);
        await Relation(a, "games_user_2", "none");
        var again = await Send(a, "games_user_2");
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/api/friends/requests/{again.Id}/reject", null)).StatusCode);
        await Relation(a, "games_user_2", "none");
        var cooldown = await ProfileEndpointsTests.Error(await a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" }), 409, "cooldown_active");
        Assert.True(cooldown.GetProperty("availableAt").GetDateTime() > again.CreatedAt);
        var back = await Send(b, "games_user_1");
        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync($"/api/friends/requests/{back.Id}")).StatusCode);
        await Relation(a, "games_user_2", "none");
        await Send(b, "games_user_1");
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=51")]
    [InlineData("pageSize=0")]
    [InlineData("page=2147483647&pageSize=50")]
    [InlineData("page=abc")]
    public async Task Lists_RejectInvalidPaginationWithStableCode(string query)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        foreach (var path in new[] { "/api/friends", "/api/friends/requests/sent", "/api/friends/requests/received" })
            await ProfileEndpointsTests.Error(await client.GetAsync(path + "?" + query), 400, "invalid_query");
    }

    [Fact]
    public async Task DefaultRateLimit_30WritesAndAllMutationRoutesShareBudget()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/friends/games_user_2")).StatusCode);
        foreach (var (method, path) in new[] { ("POST", "requests"), ("POST", "requests/1/accept"),
            ("POST", "requests/1/reject"), ("DELETE", "requests/1"), ("DELETE", "games_user_2") })
        {
            using var message = new HttpRequestMessage(new HttpMethod(method), "/api/friends/" + path);
            if (path == "requests") message.Content = JsonContent.Create(new { username = "games_user_2" });
            using var response = await a.SendAsync(message);
            await ProfileEndpointsTests.Error(response, 429, "rate_limited");
            Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        }
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/friends/summary")).StatusCode);
        await Send(b, "games_user_1");
        using var renewed = await c.LoginClientAsync();
        await ProfileEndpointsTests.Error(await renewed.DeleteAsync("/api/friends/games_user_2"), 429, "rate_limited");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentHttpSendsAndAccepts_NeverReturn500(bool reverse)
    {
        using var c = new GamesApiTestContext(concurrent: true);
        using var a = await c.AuthenticatedClientAsync();
        using var b = await c.AuthenticatedClientAsync();
        var responses = await Task.WhenAll(a.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" }),
            (reverse ? b : a).PostAsJsonAsync("/api/friends/requests", new { username = reverse ? "games_user_1" : "games_user_2" }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        var receiver = winner.User.Username == "games_user_2" ? b : a;
        var accepts = await Task.WhenAll(receiver.PostAsync($"/api/friends/requests/{winner.Id}/accept", null),
            receiver.PostAsync($"/api/friends/requests/{winner.Id}/accept", null));
        Assert.All(accepts, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, (await a.GetFromJsonAsync<FriendsSummaryDto>("/api/friends/summary"))!.FriendsCount);
    }

    [Fact]
    public async Task Swagger_DocumentsFriendsContractsAndErrors()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = document.RootElement.GetProperty("paths");
        var count = 0;
        foreach (var path in paths.EnumerateObject().Where(p => p.Name.StartsWith("/api/friends")))
            foreach (var operation in path.Value.EnumerateObject())
            {
                count++;
                Assert.Equal("Friends", operation.Value.GetProperty("tags")[0].GetString());
                var responses = operation.Value.GetProperty("responses");
                Assert.True(responses.TryGetProperty("401", out _));
                Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
            }
        Assert.Equal(9, count);
        var send = paths.GetProperty("/api/friends/requests").GetProperty("post");
        Assert.Contains("too_many_pending_requests", send.GetProperty("responses").GetProperty("409").GetProperty("description").GetString());
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SendFriendRequest");
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("username").GetProperty("type").GetString());
    }

    private static async Task<SentFriendRequestDto> Send(HttpClient client, string username)
    {
        using var response = await client.PostAsJsonAsync("/api/friends/requests", new { username });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
    }
    private static async Task Relation(HttpClient client, string username, string status, int? requestId = null)
    {
        var profile = (await client.GetFromJsonAsync<ProfileDto>("/api/users/" + username))!;
        Assert.Equal(new RelationshipDto(status, requestId), profile.Relationship);
        Assert.Equal(status == "friends" ? 1 : 0, profile.FriendsCount);
    }
}
