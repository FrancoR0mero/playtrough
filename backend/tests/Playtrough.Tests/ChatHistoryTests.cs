using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class ChatHistoryTests
{
    [Fact]
    public async Task LatestAndOlderPagesKeepStableOrderAndLosslessIds()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var group = await CreateGroupAsync(owner);
        var other = await CreateGroupAsync(owner);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var author = await UserIdAsync(db, 1);
        const long start = 9_007_199_254_740_992;
        var messages = Enumerable.Range(1, 5).Select(index => Message(group, author, start + index)).ToArray();
        db.GroupMessages.AddRange(messages);
        db.GroupMessages.Add(Message(other, author, start + 6));
        await db.SaveChangesAsync();
        var route = PathFor(group);
        using var response = await owner.GetAsync(route + "?limit=2");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var latest = (await response.Content.ReadFromJsonAsync<ChatHistoryPageDto>())!;
        Assert.Equal(new[] { "9007199254740996", "9007199254740997" }, latest.Items.Select(item => item.Id));
        Assert.Equal(latest.Items[0].Id, latest.NextCursor);
        Assert.Equal(latest.Items[^1].Id, latest.Through);
        Assert.All(latest.Items, message => Assert.Equal(DateTimeKind.Utc, message.CreatedAt.Kind));

        db.GroupMessages.Add(Message(group, author, start + 7));
        await db.SaveChangesAsync();
        var next = await ReadAsync(owner, route + "?limit=2&before=" + latest.NextCursor);
        Assert.Equal(new[] { "9007199254740994", "9007199254740995" }, next.Items.Select(item => item.Id));
        var oldest = await ReadAsync(owner, route + "?limit=2&before=" + next.NextCursor);
        Assert.Equal("9007199254740993", Assert.Single(oldest.Items).Id);
        Assert.Null(oldest.NextCursor);
    }

    [Fact]
    public async Task CatchUpKeepsAStableUpperBoundWhileNewMessagesAreWritten()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var group = await CreateGroupAsync(owner);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var author = await UserIdAsync(db, 1);
        var path = PathFor(group);
        Assert.Equal("0", (await ReadAsync(owner, path)).Through);
        db.GroupMessages.AddRange(Enumerable.Range(1, 4).Select(index => Message(group, author, index)));
        await db.SaveChangesAsync();
        var first = await ReadAsync(owner, path + "?after=0&limit=2");
        Assert.Equal(new[] { "1", "2" }, first.Items.Select(item => item.Id));
        Assert.Equal("2", first.NextCursor);
        Assert.Equal("4", first.Through);

        db.GroupMessages.Add(Message(group, author, 5));
        await db.SaveChangesAsync();
        var next = await ReadAsync(owner, path + $"?after={first.NextCursor}&through={first.Through}&limit=2");
        Assert.Equal(new[] { "3", "4" }, next.Items.Select(item => item.Id));
        Assert.Null(next.NextCursor);
        Assert.Equal("4", next.Through);
        Assert.Equal("5", Assert.Single((await ReadAsync(owner, path + "?after=4")).Items).Id);
        var empty = await ReadAsync(owner, path + "?after=5");
        Assert.Empty(empty.Items);
        Assert.Equal("5", empty.Through);
    }

    [Fact]
    public async Task HistoryChecksCurrentMembershipDeletionAndBothBlockDirections()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var member = await context.AuthenticatedClientAsync();
        using var outsider = await context.AuthenticatedClientAsync();
        using var anonymous = context.Factory.CreateClient();
        var group = await CreateGroupAsync(owner);
        var route = PathFor(group);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(anonymous.GetAsync(route)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(outsider.GetAsync(route)));
        using var joined = await member.PutAsync($"/api/groups/{group}/membership", null);
        joined.EnsureSuccessStatusCode();
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var author = await UserIdAsync(db, 1);
        var reader = await UserIdAsync(db, 2);
        db.GroupMessages.AddRange(Message(group, reader, 1), Message(group, author, 2));
        await db.SaveChangesAsync();
        foreach (var (blocker, blocked) in new[] { (reader, author), (author, reader) })
        {
            db.UserBlocks.Add(new UserBlock { BlockerId = blocker, BlockedId = blocked, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var page = await ReadAsync(member, route + "?after=0&limit=1");
            Assert.Equal(reader, Assert.Single(page.Items).AuthorId);
            Assert.Null(page.NextCursor);
            Assert.Equal("1", page.Through);
            await db.UserBlocks.ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
        }
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(member.DeleteAsync($"/api/groups/{group}/membership")));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(member.GetAsync(route + "?after=1")));
        // An author leaving the group does not erase their already persisted messages.
        Assert.Equal(2, (await ReadAsync(owner, route)).Items.Count);
        foreach (var query in new[] { "before=0", "after=-1", "before=2&after=0", "through=3", "after=4&through=3", "limit=101" })
            Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(owner.GetAsync(route + "?" + query)));
        using var profile = await owner.GetAsync($"/api/groups/{group}");
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, $"/api/groups/{group}");
        deletion.Headers.IfMatch.Add(profile.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(owner.SendAsync(deletion)));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(owner.GetAsync(route)));
    }

    private static string PathFor(int group) => $"/api/groups/{group}/messages";

    private static async Task<int> CreateGroupAsync(HttpClient owner)
    {
        using var response = await owner.PostAsJsonAsync("/api/groups",
            new { name = "Chat", description = "Historial del grupo", topic = "Retro", privacy = "public" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    private static Task<int> UserIdAsync(AppDbContext db, int user)
    {
        var username = $"games_user_{user}";
        return db.Users.Where(item => item.UserName == username).Select(item => item.Id).SingleAsync();
    }

    private static GroupMessage Message(int group, int author, long id) => new()
    {
        Id = id, GroupId = group, AuthorId = author, ClientMessageId = Guid.NewGuid(), Text = "Historial 🎮",
        CreatedAt = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc),
    };

    private static async Task<HttpStatusCode> StatusAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return response.StatusCode;
    }

    private static async Task<ChatHistoryPageDto> ReadAsync(HttpClient client, string path)
        => (await client.GetFromJsonAsync<ChatHistoryPageDto>(path))!;
}
