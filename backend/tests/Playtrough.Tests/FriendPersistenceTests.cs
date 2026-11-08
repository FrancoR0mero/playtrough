using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class FriendPersistenceTests
{
    [Fact]
    public void MySqlMigration_HasGeneratedUniquePairChecksIndexesAndNoPendingChanges()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261107232526_AddUserProfiles", "20261108000454_AddFriendships");
        Assert.Contains("`PendingPairKey` bigint AS (CASE WHEN `Status` = 0", sql);
        Assert.Contains("CREATE UNIQUE INDEX `IX_FriendRequests_PendingPairKey`", sql);
        Assert.Contains("PRIMARY KEY (`UserLowId`, `UserHighId`)", sql);
        Assert.Contains("CHECK (`UserLowId` < `UserHighId`)", sql);
        Assert.Contains("`ReceiverId`, `Status`, `CreatedAt`, `Id`", sql);
        Assert.Contains("`SenderId`, `Status`, `CreatedAt`, `Id`", sql);
        Assert.DoesNotContain("ALTER TABLE `Users`", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task UniquePendingPair_RejectsBothDirections_AllowsHistoricalRowsAndReleasesKey()
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        foreach (var reverse in new[] { false, true })
        {
            await using var db = c.CreateDb();
            db.FriendRequests.Add(new FriendRequest { SenderId = reverse ? 2 : 1, ReceiverId = reverse ? 1 : 2,
                Status = FriendRequestStatus.Pending, CreatedAt = c.Clock.UtcNow.UtcDateTime });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await c.Run(s => s.CancelAsync(1, request.Id));
        var next = await c.Run(s => s.SendRequestAsync(2, FriendTestContext.Name(1)));
        await using var check = c.CreateDb();
        Assert.Equal(2, await check.FriendRequests.CountAsync());
        Assert.Equal(4294967298L, (await check.FriendRequests.SingleAsync(r => r.Id == next.Id)).PendingPairKey);
        check.FriendRequests.AddRange(Enumerable.Range(0, 3).Select(_ => new FriendRequest
        {
            SenderId = 1, ReceiverId = 2, Status = FriendRequestStatus.Rejected,
            CreatedAt = c.Clock.UtcNow.UtcDateTime, RespondedAt = c.Clock.UtcNow.UtcDateTime,
        }));
        await check.SaveChangesAsync();
        Assert.Equal(4, await check.FriendRequests.CountAsync(r => r.PendingPairKey == null));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public async Task Friendship_CheckRejectsNonCanonicalPairs(int low, int high)
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb();
        db.Friendships.Add(new Friendship { UserLowId = low, UserHighId = high, CreatedAt = c.Clock.UtcNow.UtcDateTime });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task FriendshipPrimaryKey_RejectsDuplicateAcrossContexts()
    {
        using var c = new FriendTestContext();
        var sent = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.AcceptAsync(2, sent.Id));
        await using var db = c.CreateDb();
        db.Friendships.Add(new Friendship { UserLowId = 1, UserHighId = 2, CreatedAt = c.Clock.UtcNow.UtcDateTime });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
