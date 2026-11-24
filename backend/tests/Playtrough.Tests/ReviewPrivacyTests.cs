using System.Net.Http.Json;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Reviews;
using Xunit;

namespace Playtrough.Tests;

public class ReviewPrivacyTests
{
    [Theory]
    [InlineData("public", 0, true)]
    [InlineData("public", 1, true)]
    [InlineData("public", 2, true)]
    [InlineData("private", 0, true)]
    [InlineData("private", 1, true)]
    [InlineData("private", 2, false)]
    public async Task Reviews_RespectTheSameAccountPolicy(string visibility, int viewer, bool allowed)
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var friend = await c.AuthenticatedClientAsync();
        using var stranger = await c.AuthenticatedClientAsync();
        using var sent = await owner.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        (await friend.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        (await owner.PutAsJsonAsync($"/api/games/{game.Id}/reviews/me", new { rating = 4, text = "account review" }))
            .EnsureSuccessStatusCode();
        await PrivacyEndpointsTests.Set(owner, visibility);

        var client = new[] { owner, friend, stranger }[viewer];
        var page = (await client.GetFromJsonAsync<ReviewListResponse>($"/api/games/{game.Id}/reviews?pageSize=1"))!;
        Assert.Equal(allowed ? 1 : 0, page.Items.Count);
        Assert.False(page.HasMore);
        if (allowed) Assert.Equal("account review", page.Items[0].Text);
    }

    [Fact]
    public async Task FriendshipRevocationAndPrivacyChanges_ApplyImmediatelyToReviewPagination()
    {
        using var c = new GamesApiTestContext();
        using var owner = await c.AuthenticatedClientAsync();
        using var viewer = await c.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        var path = $"/api/games/{game.Id}/reviews";
        (await owner.PutAsJsonAsync(path + "/me", new { rating = 4, text = "private review" })).EnsureSuccessStatusCode();
        (await viewer.PutAsJsonAsync(path + "/me", new { rating = 3, text = "own review" })).EnsureSuccessStatusCode();
        await PrivacyEndpointsTests.Set(owner, "private");
        using var sent = await viewer.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_1" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        // Pending requests grant no access, and hidden rows do not create extra pages.
        var pending = (await viewer.GetFromJsonAsync<ReviewListResponse>(path + "?pageSize=1"))!;
        Assert.True(Assert.Single(pending.Items).IsMine);
        Assert.False(pending.HasMore);
        (await owner.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        Assert.Equal(2, (await viewer.GetFromJsonAsync<ReviewListResponse>(path))!.Items.Count);
        (await viewer.DeleteAsync("/api/friends/games_user_1")).EnsureSuccessStatusCode();
        Assert.Single((await viewer.GetFromJsonAsync<ReviewListResponse>(path))!.Items);
        await PrivacyEndpointsTests.Set(owner, "public");
        Assert.Equal(2, (await viewer.GetFromJsonAsync<ReviewListResponse>(path))!.Items.Count);
    }
}
