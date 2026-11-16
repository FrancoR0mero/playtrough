using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class TimelinePrivacyTests
{
    [Theory]
    [InlineData(Visibility.Public, 0, true)]
    [InlineData(Visibility.Public, 1, true)]
    [InlineData(Visibility.Public, 2, true)]
    [InlineData(Visibility.Private, 0, true)]
    [InlineData(Visibility.Private, 1, true)]
    [InlineData(Visibility.Private, 2, false)]
    [InlineData(Visibility.Friends, 0, true)]
    [InlineData(Visibility.Friends, 1, true)]
    [InlineData(Visibility.Friends, 2, false)]
    public async Task UnifiedPrivacyAndLegacyValues_ApplyEquallyToListAndPeriods(Visibility visibility, int index, bool allowed)
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync(); await c.AddAsync(hidden: true);
        await using var db = c.Store.CreateDb();
        await db.Users.Where(u => u.Id == c.Owner).ExecuteUpdateAsync(u =>
            u.SetProperty(x => x.ProfileVisibility, visibility).SetProperty(x => x.HistoryVisibility, visibility));
        db.Friendships.Add(new Friendship { UserLowId = c.Owner, UserHighId = c.Friend, CreatedAt = c.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        var viewer = new[] { c.Owner, c.Friend, c.Stranger }[index];
        if (allowed)
        {
            var list = await c.ListAsync(viewer: viewer, username: "REVIEW-1");
            var periods = await c.PeriodsAsync(viewer, "REVIEW-1");
            Assert.Equal(index == 0 ? 2 : 1, list.Items.Count);
            Assert.Equal(list.Items.Count, periods.Years.Sum(y => y.Count));
            if (index != 0) Assert.All(list.Items, e => Assert.False(e.IsHidden));
        }
        else
        {
            var error = await Assert.ThrowsAsync<TimelineException>(() => c.ListAsync(viewer: viewer, username: "review-1"));
            Assert.Equal("history_restricted", error.Code); Assert.Equal("private", error.Restriction);
            await TimelineReadTests.Error("history_restricted", () => c.PeriodsAsync(viewer, "review-1"));
        }
    }

    [Fact]
    public async Task RevokingFriendshipOrChangingPrivacy_RevokesBothViewsWithoutCaching()
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync();
        await using var db = c.Store.CreateDb();
        var service = c.Service(db);
        await db.Users.Where(u => u.Id == c.Owner).ExecuteUpdateAsync(u =>
            u.SetProperty(x => x.ProfileVisibility, Visibility.Private).SetProperty(x => x.HistoryVisibility, Visibility.Private));
        await TimelineReadTests.Error("history_restricted", () => service.ListAsync(c.Friend, "review-1", new()));
        db.Friendships.Add(new Friendship { UserLowId = c.Owner, UserHighId = c.Friend, CreatedAt = c.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        Assert.Single((await service.ListAsync(c.Friend, "review-1", new())).Items);
        await db.Friendships.ExecuteDeleteAsync();
        await TimelineReadTests.Error("history_restricted", () => service.ListAsync(c.Friend, "review-1", new()));
        await TimelineReadTests.Error("history_restricted", () => service.PeriodsAsync(c.Friend, "review-1", null));
        await db.Users.Where(u => u.Id == c.Owner).ExecuteUpdateAsync(u =>
            u.SetProperty(x => x.ProfileVisibility, Visibility.Public).SetProperty(x => x.HistoryVisibility, Visibility.Public));
        Assert.Single((await service.ListAsync(c.Friend, "review-1", new())).Items);
    }

    [Fact]
    public async Task MissingUnconfirmedAndBlockedAccounts_AreNotDisclosed_WhileOwnerKeepsAccess()
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync(hidden: true);
        await using var db = c.Store.CreateDb();
        await db.Users.Where(u => u.Id == c.Owner).ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailConfirmed, false));
        await TimelineReadTests.Error("user_not_found", () => c.ListAsync(viewer: c.Stranger, username: "review-1"));
        await TimelineReadTests.Error("user_not_found", () => c.PeriodsAsync(c.Stranger, "review-1"));
        await TimelineReadTests.Error("user_not_found", () => c.ListAsync(username: "missing"));
        Assert.True(Assert.Single((await c.ListAsync(username: "review-1")).Items).IsHidden);
        await db.Users.Where(u => u.Id == c.Owner).ExecuteUpdateAsync(u => u.SetProperty(x => x.EmailConfirmed, true));
        db.UserBlocks.Add(new UserBlock { BlockerId = c.Stranger, BlockedId = c.Owner, CreatedAt = c.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await TimelineReadTests.Error("user_not_found", () => c.ListAsync(viewer: c.Stranger, username: "review-1"));
        await TimelineReadTests.Error("user_not_found", () => c.PeriodsAsync(c.Stranger, "review-1"));
    }
}
