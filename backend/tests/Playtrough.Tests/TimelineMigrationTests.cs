using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class TimelineMigrationTests
{
    [Fact]
    public void MySqlMigration_IsAdditive_HasRequiredIndexesForeignKeysAndStableModel()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261113070456_AddUserBlocks", "AddTimelineEntries");
        Assert.Contains("CREATE TABLE `TimelineEntries`", sql);
        Assert.Contains("`Id` bigint NOT NULL AUTO_INCREMENT", sql);
        Assert.Contains("`OccurredOn` date NOT NULL", sql);
        Assert.Contains("`RecordedAt` datetime(6) NOT NULL", sql);
        Assert.Contains("`IsHidden` tinyint(1) NOT NULL DEFAULT FALSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE UNIQUE INDEX `IX_TimelineEntries_LibraryEventId`", sql);
        Assert.Contains("CREATE UNIQUE INDEX `IX_TimelineEntries_ReviewId`", sql);
        Assert.Contains("IX_TimelineEntries_UserId_OccurredOn_RecordedAt_Id", sql);
        Assert.Contains("IX_TimelineEntries_FeedEligible_UserId_RecordedAt_Id", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("ALTER TABLE `Reviews`", sql);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        var entity = db.Model.FindEntityType(typeof(TimelineEntry))!;
        Assert.Equal(typeof(long), entity.FindProperty(nameof(TimelineEntry.Id))!.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(Game)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(Review)).DeleteBehavior);
        Assert.All(entity.GetIndexes().Where(i => i.IsUnique), index => Assert.True(index.Properties.Single().IsNullable));
        var mapping = entity.FindProperty(nameof(TimelineEntry.OccurredOn))!.GetRelationalTypeMapping();
        Assert.Equal(typeof(DateTime), mapping.Converter!.ProviderClrType);
        Assert.Equal(new DateTime(2020, 2, 29), mapping.Converter.ConvertToProvider(new DateOnly(2020, 2, 29)));
    }
}
