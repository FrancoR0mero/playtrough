using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class GroupReviewsEndpointsTests
{
    [Fact]
    public async Task ReviewsRequireCurrentMembershipAndExcludeReviewsOfOutsiders()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var reader = await context.AuthenticatedClientAsync();
        using var anonymous = context.Factory.CreateClient();
        var group = await CreateGroup(owner, "private");
        var url = group + "/reviews";
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        var review = await Review(reader, game.Id, "Solo para el grupo si ingreso");
        await Expect(anonymous.GetAsync(url), HttpStatusCode.Unauthorized);
        await Expect(reader.GetAsync(url), HttpStatusCode.NotFound);
        var empty = (await owner.GetFromJsonAsync<GroupReviewPageDto>(url))!;
        Assert.Empty(empty.Items);
        Assert.Null(empty.NextCursor);
        await Join(reader, group, "pending");
        await Expect(reader.GetAsync(url), HttpStatusCode.NotFound);

        var request = Assert.Single((await owner.GetFromJsonAsync<GroupPeoplePageDto<GroupJoinRequestDto>>(
            group + "/join-requests"))!.Items);
        await Expect(owner.PostAsJsonAsync($"{group}/join-requests/{request.Id}/decision", new { decision = "approve" }),
            HttpStatusCode.NoContent);

        using var response = await reader.GetAsync(url);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var page = (await response.Content.ReadFromJsonAsync<GroupReviewPageDto>())!;
        var item = Assert.Single(page.Items);
        Assert.Equal(new GroupReviewDto(review.Id, game.Id, game.Name, "games_user_2",
            4.5m, "Solo para el grupo si ingreso", review.CreatedAt, null), item);
        await Expect(owner.DeleteAsync($"{group}/members/{request.UserId}"), HttpStatusCode.NoContent);
        await Expect(reader.GetAsync(url), HttpStatusCode.NotFound);
        Assert.Empty((await owner.GetFromJsonAsync<GroupReviewPageDto>(url))!.Items);
    }

    [Fact]
    public async Task PrivacyAndBothBlockDirectionsApplyBeforePagination()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var reader = await context.AuthenticatedClientAsync();
        using var author = await context.AuthenticatedClientAsync();
        var group = await CreateGroup(owner);
        await Join(reader, group);
        await Join(author, group);
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        var initial = await Review(reader, game.Id, null);
        var newer = await Review(author, game.Id, "Reseña de cuenta privada");
        await Expect(author.PutAsJsonAsync("/api/users/me/privacy", new { visibility = "private" }), HttpStatusCode.OK);
        var url = group + "/reviews?limit=1";
        var restricted = (await reader.GetFromJsonAsync<GroupReviewPageDto>(url))!;
        Assert.Equal(initial.Id, Assert.Single(restricted.Items).Id);
        Assert.Null(restricted.NextCursor);

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var readerId = await UserId(db, "games_user_2");
        var authorId = await UserId(db, "games_user_3");
        var now = DateTime.UtcNow;
        db.Friendships.Add(new Friendship { UserLowId = Math.Min(readerId, authorId), UserHighId = Math.Max(readerId, authorId), CreatedAt = now });
        await db.SaveChangesAsync();
        var first = (await reader.GetFromJsonAsync<GroupReviewPageDto>(url))!;
        Assert.Equal(newer.Id, Assert.Single(first.Items).Id);
        Assert.Equal(newer.Id, first.NextCursor);
        var next = (await reader.GetFromJsonAsync<GroupReviewPageDto>($"{url}&cursor={first.NextCursor}"))!;
        Assert.Equal(initial.Id, Assert.Single(next.Items).Id);
        Assert.Null(next.NextCursor);

        foreach (var (blocker, blocked) in new[] { (readerId, authorId), (authorId, readerId) })
        {
            db.UserBlocks.Add(new UserBlock { BlockerId = blocker, BlockedId = blocked, CreatedAt = now });
            await db.SaveChangesAsync();
            var blockedPage = (await reader.GetFromJsonAsync<GroupReviewPageDto>(url))!;
            Assert.Equal(initial.Id, Assert.Single(blockedPage.Items).Id);
            Assert.Null(blockedPage.NextCursor);
            await db.UserBlocks.ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task ViewReflectsReviewDeletionAndChangesInMembership()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var author = await context.AuthenticatedClientAsync();
        var group = await CreateGroup(owner);
        var url = group + "/reviews";
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        var review = await Review(author, game.Id, "Actualizable");
        await Join(author, group);
        Assert.Equal(review.Id, Assert.Single((await owner.GetFromJsonAsync<GroupReviewPageDto>(url))!.Items).Id);
        await Expect(author.DeleteAsync(group + "/membership"), HttpStatusCode.NoContent);
        await Expect(author.GetAsync(url), HttpStatusCode.NotFound);
        Assert.Empty((await owner.GetFromJsonAsync<GroupReviewPageDto>(url))!.Items);

        await Join(author, group);
        Assert.Equal(review.Id, Assert.Single((await owner.GetFromJsonAsync<GroupReviewPageDto>(url))!.Items).Id);
        await Expect(author.DeleteAsync($"/api/games/{game.Id}/reviews/me"), HttpStatusCode.OK);
        Assert.Empty((await owner.GetFromJsonAsync<GroupReviewPageDto>(url))!.Items);
        await Expect(owner.GetAsync(url + "?limit=51"), HttpStatusCode.BadRequest);
        await Expect(owner.GetAsync(url + "?cursor=0"), HttpStatusCode.BadRequest);
        await Expect(owner.GetAsync("/api/groups/2147483647/reviews"), HttpStatusCode.NotFound);
    }

    private static Task<int> UserId(AppDbContext db, string username)
        => db.Users.Where(user => user.UserName == username).Select(user => user.Id).SingleAsync();

    private static async Task<string> CreateGroup(HttpClient owner, string privacy = "public")
    {
        using var response = await owner.PostAsJsonAsync("/api/groups",
            new { name = "Grupo de prueba", description = "Descripción", genreId = "rpg",
                subgenreIds = new[] { "jrpg" }, privacy });
        response.EnsureSuccessStatusCode();
        return response.Headers.Location!.AbsolutePath;
    }

    private static async Task Join(HttpClient reader, string group, string expected = "member")
    {
        using var response = await reader.PutAsync(group + "/membership", null);
        response.EnsureSuccessStatusCode();
        Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<GroupMembershipDto>())!.Status);
    }

    private static async Task<MyReviewDto> Review(HttpClient author, int gameId, string? text)
    {
        using var response = await author.PutAsJsonAsync($"/api/games/{gameId}/reviews/me", new { rating = 4.5m, text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UpsertReviewResponse>())!.Review;
    }

    private static async Task Expect(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        Assert.Equal(status, response.StatusCode);
    }
}
