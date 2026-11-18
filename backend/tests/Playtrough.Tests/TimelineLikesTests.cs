using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public sealed class TimelineLikesTests
{
    [Fact]
    public async Task PagesFollowCreationTimeAndUserIdAndExcludeDislike()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        var otherEntry = await context.AddAsync();
        await using var db = context.Store.CreateDb();
        var timeline = context.Service(db);
        var users = context.Store.UserIds;
        var now = context.Store.Clock.UtcNow;
        await ReactAsync(context, db, entry.Id, users[1], "like");
        await ReactAsync(context, db, entry.Id, users[5], "dislike");
        context.Store.Clock.UtcNow = now.AddSeconds(1);
        await ReactAsync(context, db, entry.Id, users[2], "like");
        await ReactAsync(context, db, entry.Id, users[3], "like");

        context.Store.Observer.Commands.Clear();
        var page = await timeline.LikesAsync(context.Owner, entry.Id, new() { Limit = 1 }, default);
        Assert.Single(context.Store.Observer.Commands);
        Assert.Equal("review-4", Assert.Single(page.Items).User.Username);
        Assert.Equal(now.AddSeconds(1).UtcDateTime, Assert.Single(page.Items).CreatedAt);
        Assert.True(page.HasMore);
        Assert.NotNull(page.NextCursor);

        context.Store.Clock.UtcNow = now.AddSeconds(2);
        await ReactAsync(context, db, entry.Id, users[4], "like");
        var next = await timeline.LikesAsync(context.Owner, entry.Id, new() { Limit = 2, Cursor = page.NextCursor }, default);
        Assert.Equal(new[] { "review-3", "review-2" }, next.Items.Select(row => row.User.Username));
        Assert.False(next.HasMore);
        Assert.Null(next.NextCursor);
        await TimelineReadTests.Error("invalid_cursor", () => timeline.LikesAsync(users[1], entry.Id, new() { Cursor = page.NextCursor }, default));
        await TimelineReadTests.Error("invalid_cursor", () => timeline.LikesAsync(context.Owner, otherEntry.Id, new() { Cursor = page.NextCursor }, default));

        await ReactAsync(context, db, entry.Id, users[3], "dislike");
        var refreshed = await timeline.LikesAsync(context.Owner, entry.Id, new(), default);
        Assert.Equal(new[] { "review-5", "review-3", "review-2" }, refreshed.Items.Select(row => row.User.Username));
    }

    [Fact]
    public async Task FiltersBothBlockDirectionsAndUnconfirmedBeforeTakingThePage()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        var users = context.Store.UserIds;
        foreach (var actor in users.Skip(2).Take(4))
            await ReactAsync(context, db, entry.Id, actor, "like");
        db.UserBlocks.AddRange(
            new UserBlock { BlockerId = users[1], BlockedId = users[4], CreatedAt = context.Store.Clock.UtcNow.UtcDateTime },
            new UserBlock { BlockerId = users[5], BlockedId = users[1], CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await db.Users.Where(user => user.Id == users[3]).ExecuteUpdateAsync(update => update.SetProperty(user => user.EmailConfirmed, false));
        var timeline = context.Service(db);
        var page = await timeline.LikesAsync(users[1], entry.Id, new() { Limit = 1 }, default);
        Assert.Equal("review-3", Assert.Single(page.Items).User.Username);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
        Assert.Equal(4, (await timeline.DetailAsync(users[1], entry.Id, default)).LikeCount);
    }

    [Fact]
    public async Task UsesEntryVisibilityAndDistinguishesAnEmptyListFromDeniedAccess()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        var timeline = context.Service(db);
        var empty = await timeline.LikesAsync(context.Stranger, entry.Id, new(), default);
        Assert.Empty(empty.Items);
        Assert.False(empty.HasMore);
        Assert.Null(empty.NextCursor);
        await TimelineReadTests.Error("entry_not_found", () => timeline.LikesAsync(context.Owner, long.MaxValue, new(), default));

        db.Friendships.Add(new() { UserLowId = context.Owner, UserHighId = context.Friend, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await db.Users.Where(user => user.Id == context.Owner).ExecuteUpdateAsync(update => update.SetProperty(user => user.ProfileVisibility, Visibility.Private));
        await TimelineReadTests.Error("entry_not_found", () => timeline.LikesAsync(context.Stranger, entry.Id, new(), default));
        Assert.Empty((await timeline.LikesAsync(context.Friend, entry.Id, new(), default)).Items);

        await db.TimelineEntries.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsHidden, true));
        Assert.Empty((await timeline.LikesAsync(context.Owner, entry.Id, new(), default)).Items);
        await TimelineReadTests.Error("entry_not_found", () => timeline.LikesAsync(context.Friend, entry.Id, new(), default));
        await db.TimelineEntries.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsHidden, false));
        db.UserBlocks.Add(new() { BlockerId = context.Friend, BlockedId = context.Owner, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await TimelineReadTests.Error("entry_not_found", () => timeline.LikesAsync(context.Friend, entry.Id, new(), default));
    }

    [Fact]
    public void MySqlTranslatesKeysetFiltersAndTheProjectionInOneStatement()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var query = new TimelineQuery(db, Options.Create(new TimelineSettings()), TimeProvider.System);
        var sql = query.EntryLikes(new TimelineEntryAccess(db).VisibleTo(21), 21, 81,
            new(DateOnly.MinValue, new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc), 34), 20).ToQueryString();
        foreach (var token in new[] { "LEFT JOIN", "TimelineEntryReactions", "UserBlocks", "EmailConfirmed", "CreatedAt", "UserId", "ORDER BY", "LIMIT" })
            Assert.Contains(token, sql);
        Assert.DoesNotContain("PasswordHash", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    private static Task ReactAsync(TimelineReadTestContext context, AppDbContext db, long entryId, int actor, string value)
        => new ReactionService(db, context.Store.Clock, new InMemoryDomainEventPublisher()).SetReactionAsync(actor, entryId,
            new TimelineReactionTarget(db, new(db)), new() { Value = JsonSerializer.SerializeToElement(value) }, default);
}
