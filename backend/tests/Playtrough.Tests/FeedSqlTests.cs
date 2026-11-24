using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class FeedSqlTests
{
    [Fact]
    public void OracleMySql_TranslatesBothFriendDirectionsKeysetAndIndexableEligibility()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var clock = new CatalogPersistenceClock();
        var query = new TimelineQuery(db, Options.Create(new TimelineSettings()), clock);
        var sql = query.FeedEntries(1, new(clock.UtcNow.UtcDateTime, 42), 20).ToQueryString();
        Assert.Contains("UNION ALL", sql);
        Assert.Contains("`FeedEligible` = @", sql);
        Assert.Contains("UserLowId", sql); Assert.Contains("UserHighId", sql);
        Assert.Contains("UserBlocks", sql); Assert.Contains("EmailConfirmed", sql);
        Assert.Contains("RecordedAt", sql); Assert.Contains("ORDER BY", sql); Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("PasswordHash", sql); Assert.DoesNotContain("ProviderDetailsJson", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
