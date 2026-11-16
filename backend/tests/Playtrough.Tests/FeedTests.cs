using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class FeedTests
{
    private static FeedService Service(TimelineReadTestContext c, Playtrough.Api.Data.AppDbContext db) => new(c.Query(db), c.Cursors);
    private static async Task<FeedPageDto> Feed(TimelineReadTestContext c, int? viewer = null, FeedListRequest? request = null)
    {
        await using var db = c.Store.CreateDb();
        return await Service(c, db).ListAsync(viewer ?? c.Owner, request ?? new());
    }
    private static async Task Friend(TimelineReadTestContext c, int first, int second)
    {
        await using var db = c.Store.CreateDb(false);
        db.Friendships.Add(new Friendship { UserLowId = Math.Min(first, second), UserHighId = Math.Max(first, second), CreatedAt = c.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Feed_OnlyShowsConfirmedUnblockedFriends_AndHonorsUnifiedPrivateAccounts()
    {
        using var c = new TimelineReadTestContext();
        var viewer = c.Store.UserIds[5];
        await Friend(c, viewer, c.Owner); // Friend is below viewer in the canonical pair.
        await Friend(c, viewer, c.Store.UserIds[8]); // Friend is above viewer.
        await Friend(c, viewer, c.Friend);
        await Friend(c, viewer, c.Store.UserIds[3]);
        await Friend(c, viewer, c.Store.UserIds[4]);
        var visible = await c.AddAsync(owner: c.Owner);
        var privateEntry = await c.AddAsync(owner: c.Store.UserIds[8]);
        await c.AddAsync(owner: viewer);
        await c.AddAsync(owner: c.Stranger);
        await c.AddAsync(owner: c.Owner, hidden: true);
        await c.AddAsync(owner: c.Friend);
        await c.AddAsync(owner: c.Store.UserIds[3]);
        await c.AddAsync(owner: c.Store.UserIds[4]);
        await c.AddAsync("2020-01-01", owner: c.Owner); // Not feed eligible.
        await using (var db = c.Store.CreateDb(false))
        {
            await db.Users.Where(u => u.Id == c.Store.UserIds[8]).ExecuteUpdateAsync(u =>
                u.SetProperty(x => x.ProfileVisibility, Visibility.Private).SetProperty(x => x.HistoryVisibility, Visibility.Private));
            await db.Users.Where(u => u.Id == c.Friend).ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailConfirmed, false));
            db.UserBlocks.AddRange(
                new UserBlock { BlockerId = viewer, BlockedId = c.Store.UserIds[3], CreatedAt = c.Store.Clock.UtcNow.UtcDateTime },
                new UserBlock { BlockerId = c.Store.UserIds[4], BlockedId = viewer, CreatedAt = c.Store.Clock.UtcNow.UtcDateTime });
            await db.SaveChangesAsync();
        }
        var page = await Feed(c, viewer);
        Assert.Equal(new[] { privateEntry.Id, visible.Id }, page.Items.Select(e => e.Id));
        Assert.All(page.Items, e => Assert.False(e.IsHidden));
        Assert.False(page.HasMore); Assert.Null(page.NextCursor);
        Assert.Contains((await c.ListAsync()).Items, e => e.OccurredOn.Year == 2020);
    }

    [Fact]
    public async Task Feed_OrdersByRegistrationRatherThanEffectiveDate_AndPaginatesWithoutRepeats()
    {
        using var c = new TimelineReadTestContext();
        await Friend(c, c.Owner, c.Friend);
        var now = c.Store.Clock.UtcNow.UtcDateTime;
        var yesterday = await c.AddAsync("2026-10-30", now.AddDays(-1), c.Friend);
        var retro = await c.AddAsync("2026-10-28", now, c.Friend);
        var tie = await c.AddAsync("2026-10-28", now, c.Friend);
        var page = await Feed(c, request: new() { Limit = 1 });
        Assert.Equal(tie.Id, Assert.Single(page.Items).Id);
        Assert.True(page.HasMore);
        await c.AddAsync("2026-10-30", now.AddHours(1), c.Friend);
        var seen = new List<long> { tie.Id };
        while (page.NextCursor is { } cursor)
        {
            page = await Feed(c, request: new() { Limit = 1, Cursor = cursor });
            seen.AddRange(page.Items.Select(e => e.Id));
        }
        Assert.Equal(new[] { tie.Id, retro.Id, yesterday.Id }, seen);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task FeedEligibility_IsStoredAtCreation_AndBoundaryIsInclusive()
    {
        using var c = new TimelineReadTestContext();
        await Friend(c, c.Owner, c.Friend);
        var within = await c.AddAsync("2026-10-23", owner: c.Friend);
        var outside = await c.AddAsync("2026-10-22", owner: c.Friend);
        Assert.True(within.FeedEligible); Assert.False(outside.FeedEligible);
        c.Store.Clock.UtcNow = c.Store.Clock.UtcNow.AddYears(1);
        Assert.Equal(within.Id, Assert.Single((await Feed(c)).Items).Id);
        var friendTimeline = await c.ListAsync(viewer: c.Owner, username: "review-2");
        Assert.Equal(2, friendTimeline.Items.Count);
    }

    [Fact]
    public async Task FriendshipChangesAndVisibility_ImmediatelyChangeFeedWithoutCaching()
    {
        using var c = new TimelineReadTestContext();
        var entry = await c.AddAsync(owner: c.Friend);
        await using var db = c.Store.CreateDb();
        var service = Service(c, db);
        Assert.Empty((await service.ListAsync(c.Owner, new())).Items);
        await Friend(c, c.Owner, c.Friend);
        Assert.Single((await service.ListAsync(c.Owner, new())).Items);
        await c.Service(db).SetVisibilityAsync(c.Friend, entry.Id, new() { Hidden = System.Text.Json.JsonSerializer.SerializeToElement(true) });
        Assert.Empty((await service.ListAsync(c.Owner, new())).Items);
        await c.Service(db).SetVisibilityAsync(c.Friend, entry.Id, new() { Hidden = System.Text.Json.JsonSerializer.SerializeToElement(false) });
        Assert.Single((await service.ListAsync(c.Owner, new())).Items);
        await db.Friendships.ExecuteDeleteAsync();
        Assert.Empty((await service.ListAsync(c.Owner, new())).Items);
    }

    [Fact]
    public async Task Feed_ProjectsCurrentAuthorGameAndReview_AndUsesOneSqlAsFriendsAndEntriesGrow()
    {
        using var c = new TimelineReadTestContext();
        await Friend(c, c.Owner, c.Friend);
        await c.Store.UpsertAsync(4, "one two three four", userId: c.Friend);
        c.Settings.ExcerptLength = 10;
        await using var db = c.Store.CreateDb();
        var avatar = new string('a', 32) + ".webp";
        await db.Users.Where(u => u.Id == c.Friend).ExecuteUpdateAsync(u => u.SetProperty(x => x.AvatarFileName, avatar));
        c.Store.Observer.Commands.Clear();
        var page = await Service(c, db).ListAsync(c.Owner, new());
        Assert.Single(c.Store.Observer.Commands);
        var item = Assert.Single(page.Items);
        Assert.Equal("review-2", item.Author.Username);
        Assert.Equal("/api/avatars/" + avatar, item.Author.AvatarUrl);
        Assert.Equal(c.Store.GameId, item.Game.GameId);
        Assert.Equal(4, item.Review!.Rating); Assert.Equal("one two…", item.Review.Excerpt);
        await c.Store.UpsertAsync(5, "edited", userId: c.Friend);
        Assert.Equal(5, Assert.Single((await Feed(c)).Items).Review!.Rating);
        for (var i = 2; i < 10; i++)
        {
            await Friend(c, c.Owner, c.Store.UserIds[i]);
            await c.AddAsync(owner: c.Store.UserIds[i]);
        }
        c.Store.Observer.Commands.Clear();
        var many = await Service(c, db).ListAsync(c.Owner, new() { Limit = 50 });
        Assert.Equal(9, many.Items.Count);
        var sql = Assert.Single(c.Store.Observer.Commands);
        Assert.Contains("UNION ALL", sql); Assert.Contains("JOIN", sql);
        Assert.DoesNotContain("PasswordHash", sql); Assert.DoesNotContain("ProviderDetailsJson", sql);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Feed_EmptyWithNoFriendsOrFriendsWithoutActivity()
    {
        using var c = new TimelineReadTestContext();
        var empty = await Feed(c);
        Assert.Empty(empty.Items); Assert.False(empty.HasMore); Assert.Null(empty.NextCursor);
        await Friend(c, c.Owner, c.Friend);
        empty = await Feed(c);
        Assert.Empty(empty.Items); Assert.False(empty.HasMore); Assert.Null(empty.NextCursor);
    }

    [Fact]
    public async Task Feed_CursorsRejectTamperingDifferentViewersAndTimelinePositions()
    {
        using var c = new TimelineReadTestContext();
        await Friend(c, c.Owner, c.Friend);
        await c.AddAsync(owner: c.Friend); await c.AddAsync(owner: c.Friend);
        var cursor = (await Feed(c, request: new() { Limit = 1 })).NextCursor!;
        foreach (var invalid in new[] { "", "bad", "v2." + cursor[3..], cursor + "=", cursor[..20] + (cursor[20] == 'a' ? 'b' : 'a') + cursor[21..] })
            await TimelineReadTests.Error("invalid_cursor", () => Feed(c, request: new() { Cursor = invalid }));
        await TimelineReadTests.Error("invalid_cursor", () => Feed(c, c.Stranger, new() { Cursor = cursor }));
        await TimelineReadTests.Error("invalid_cursor", () => c.ListAsync(new() { Cursor = cursor }));
        var timeline = (await c.ListAsync(new() { Limit = 1 }, c.Friend)).NextCursor!;
        await TimelineReadTests.Error("invalid_cursor", () => Feed(c, request: new() { Cursor = timeline }));
        var wrongKey = c.Cursors.Encode(new(new DateOnly(2026, 1, 1), c.Store.Clock.UtcNow.UtcDateTime, 1), $"feed:{c.Owner}");
        await TimelineReadTests.Error("invalid_cursor", () => Feed(c, request: new() { Cursor = wrongKey }));
    }
}
