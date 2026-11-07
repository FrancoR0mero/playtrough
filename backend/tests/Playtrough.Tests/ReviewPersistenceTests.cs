using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class ReviewPersistenceTests
{
    [Fact]
    public void MySqlModel_HasExactDecimalTypesIndexesConcurrencyAndDeleteBehaviors()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var review = db.Model.FindEntityType(typeof(Review))!;
        var game = db.Model.FindEntityType(typeof(Game))!;
        Assert.Equal("decimal(2,1)", review.FindProperty(nameof(Review.Rating))!.GetColumnType());
        Assert.Equal("decimal(12,1)", game.FindProperty(nameof(Game.RatingSum))!.GetColumnType());
        Assert.Equal(0, game.FindProperty(nameof(Game.RatingCount))!.GetDefaultValue());
        Assert.Equal(0m, game.FindProperty(nameof(Game.RatingSum))!.GetDefaultValue());
        var text = review.FindProperty(nameof(Review.Text))!;
        Assert.True(text.IsNullable);
        Assert.Equal(2000, text.GetMaxLength());
        Assert.Equal("utf8mb4", text.FindAnnotation("MySQL:Charset")!.Value);
        Assert.True(review.FindProperty(nameof(Review.ConcurrencyToken))!.IsConcurrencyToken);
        Assert.Contains(review.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "UserId", "GameId" }));
        Assert.Contains(review.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "GameId", "CreatedAt", "Id" }));
        Assert.Contains(review.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "GameId", "Rating" }));
        Assert.Equal(DeleteBehavior.Cascade, review.GetForeignKeys().Single(key =>
            key.PrincipalEntityType.ClrType == typeof(ApplicationUser)).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, review.GetForeignKeys().Single(key =>
            key.PrincipalEntityType.ClrType == typeof(Game)).DeleteBehavior);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void MySqlMigration_OnlyAddsReviewSchemaAndZeroDefaults_WithExplicitUtf8mb4Text()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var script = db.GetService<IMigrator>().GenerateScript("20261028004629_AddLibrary", "20261031000047_AddReviews");
        Assert.Contains("ALTER TABLE `Games` ADD `RatingCount` int NOT NULL DEFAULT 0", script);
        Assert.Contains("ALTER TABLE `Games` ADD `RatingSum` decimal(12,1) NOT NULL DEFAULT 0.0", script);
        Assert.Contains("CREATE TABLE `Reviews`", script);
        Assert.Contains("`Rating` decimal(2,1) NOT NULL", script);
        Assert.Contains("`Text` varchar(2000) CHARACTER SET utf8mb4 NULL", script);
        Assert.Contains("ON DELETE CASCADE", script);
        Assert.Contains("ON DELETE RESTRICT", script);
        Assert.Contains("CREATE UNIQUE INDEX `IX_Reviews_UserId_GameId`", script);
        Assert.Contains("CREATE INDEX `IX_Reviews_GameId_CreatedAt_Id`", script);
        Assert.Contains("CREATE INDEX `IX_Reviews_GameId_Rating`", script);
        Assert.Contains("CREATE TRIGGER `TR_Users_DeleteReviewRatings` BEFORE DELETE ON `Users`", script);
        Assert.DoesNotContain("DROP TABLE", script);
        Assert.DoesNotContain("CREATE TABLE `Users`", script);
        Assert.DoesNotContain("ALTER TABLE `Users`", script);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task UniqueIndexAndForeignKeys_AreEnforcedByDatabase()
    {
        using var context = new ReviewTestContext();
        var original = (await context.UpsertAsync()).Review;
        await using (var db = context.CreateDb())
        {
            db.Reviews.Add(new Review
            {
                UserId = original.UserId, GameId = original.GameId, Rating = 2m,
                CreatedAt = original.CreatedAt, ConcurrencyToken = Guid.NewGuid(),
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = context.CreateDb())
        {
            db.Games.Remove((await db.Games.FindAsync(context.GameId))!);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = context.CreateDb())
        {
            db.Users.Remove((await db.Users.FindAsync(context.UserIds[0]))!);
            await db.SaveChangesAsync();
            Assert.Empty(await db.Reviews.ToListAsync());
        }
        var game = await context.GameAsync();
        Assert.Equal(0, game.RatingCount);
        Assert.Equal(0m, game.RatingSum);
    }

    [Fact]
    public async Task UserCascade_AdjustsAllTheirGames_AndPreservesOtherAuthorsAndUnrelatedGames()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(4.5m);
        await context.UpsertAsync(2m, userId: context.UserIds[1]);
        await using var db = context.CreateDb();
        var second = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime, 43);
        var unrelated = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime, 44);
        db.Games.AddRange(second, unrelated);
        await db.SaveChangesAsync();
        await context.Service(db).UpsertAsync(context.UserIds[0], second.Id, 3.5m, null);
        db.Users.Remove((await db.Users.FindAsync(context.UserIds[0]))!);
        await db.SaveChangesAsync();
        var surviving = Assert.Single(await db.Reviews.AsNoTracking().ToListAsync());
        Assert.Equal(context.UserIds[1], surviving.UserId);
        Assert.Equal(new RatingSummary(2m, 1), await context.Service(db).GetSummaryAsync(context.GameId));
        Assert.Equal(new RatingSummary(null, 0), await context.Service(db).GetSummaryAsync(second.Id));
        Assert.Equal(new RatingSummary(null, 0), await context.Service(db).GetSummaryAsync(unrelated.Id));
        var games = await db.Games.AsNoTracking().ToListAsync();
        Assert.Equal(2m, games.Sum(game => game.RatingSum));
        Assert.Equal(1, games.Sum(game => game.RatingCount));
    }

    [Fact]
    public async Task UserCascade_RollsBackCounterAdjustment_WhenAnotherForeignKeyBlocksDeletion()
    {
        using var context = new ReviewTestContext();
        var original = await context.UpsertAsync(4.5m);
        await using var db = context.CreateDb();
        db.RoleChangeLogs.Add(new RoleChangeLog
        {
            ChangedByUserId = context.UserIds[0], TargetUserId = context.UserIds[1],
            OldRole = "User", NewRole = "Moderator", ChangedAt = context.Clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync();
        // Keep the dependent untracked so the database FK, rather than EF's client
        // relationship validation, rejects the deletion after the trigger executed.
        db.ChangeTracker.Clear();
        db.Users.Remove((await db.Users.FindAsync(context.UserIds[0]))!);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(original.Review.ConcurrencyToken, (await context.MineAsync()).Review.ConcurrencyToken);
        Assert.Equal(1, (await context.GameAsync()).RatingCount);
        Assert.Equal(4.5m, (await context.GameAsync()).RatingSum);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task SummaryFailure_RollsBackReviewAndCounters_AndLeavesNoPendingWrites(string operation)
    {
        using var context = new ReviewTestContext();
        var original = operation == "create" ? null : await context.UpsertAsync(2.5m, "original");
        var before = await context.GameAsync();
        context.Observer.BeforeSummaryRead = _ => throw new InvalidOperationException("Simulated summary failure");
        await using var db = context.CreateDb();
        var service = context.Service(db);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "delete") await service.DeleteAsync(context.UserIds[0], context.GameId);
            else await service.UpsertAsync(context.UserIds[0], context.GameId, 5m, "changed");
        });
        Assert.Equal("Simulated summary failure", error.Message);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await db.SaveChangesAsync());
        var game = await context.GameAsync();
        Assert.Equal(before.RatingCount, game.RatingCount);
        Assert.Equal(before.RatingSum, game.RatingSum);
        var rows = await db.Reviews.AsNoTracking().ToListAsync();
        if (original is null) Assert.Empty(rows);
        else
        {
            var persisted = Assert.Single(rows);
            Assert.Equal(original.Review.ConcurrencyToken, persisted.ConcurrencyToken);
            Assert.Equal(original.Review.Rating, persisted.Rating);
            Assert.Equal(original.Review.Text, persisted.Text);
            Assert.Null(persisted.EditedAt);
        }
    }

    [Fact]
    public async Task DeleteGuard_RejectsInconsistentCounters_WithoutGoingNegativeOrDeletingReview()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(4.5m);
        await using (var corrupt = context.CreateDb())
            await corrupt.Games.ExecuteUpdateAsync(update => update.SetProperty(game => game.RatingCount, 0)
                .SetProperty(game => game.RatingSum, 0m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.DeleteAsync());
        Assert.Equal(4.5m, (await context.MineAsync()).Review.Rating);
        var game = await context.GameAsync();
        Assert.Equal(0, game.RatingCount);
        Assert.Equal(0m, game.RatingSum);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task ReviewWriteFailure_RollsBackEarlierAtomicCounterUpdate(string operation)
    {
        using var context = new ReviewTestContext();
        if (operation != "create") await context.UpsertAsync(2.5m);
        var before = await context.GameAsync();
        await using var db = context.CreateDb();
        var triggerSql = operation switch
        {
            "create" => "CREATE TRIGGER fail_review_write BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'Simulated review failure'); END;",
            "edit" => "CREATE TRIGGER fail_review_write BEFORE UPDATE ON Reviews BEGIN SELECT RAISE(ABORT, 'Simulated review failure'); END;",
            _ => "CREATE TRIGGER fail_review_write BEFORE DELETE ON Reviews BEGIN SELECT RAISE(ABORT, 'Simulated review failure'); END;",
        };
        // A real database error after the counter statement, inside the same transaction.
        await db.Database.ExecuteSqlRawAsync(triggerSql);
        var service = context.Service(db);
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (operation == "delete") await service.DeleteAsync(context.UserIds[0], context.GameId);
            else await service.UpsertAsync(context.UserIds[0], context.GameId, 5m, null);
        });
        Assert.Empty(db.ChangeTracker.Entries());
        var game = await context.GameAsync();
        Assert.Equal(before.RatingCount, game.RatingCount);
        Assert.Equal(before.RatingSum, game.RatingSum);
        if (operation == "create") Assert.Empty(await db.Reviews.ToListAsync());
        else Assert.Equal(2.5m, (await context.MineAsync()).Review.Rating);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task RetryingExecutionStrategy_ReplaysWholeTransaction_WithoutDoubleCounting(string operation)
    {
        using var context = new ReviewTestContext();
        if (operation != "create") await context.UpsertAsync(2.5m);
        var attempts = 0;
        context.Observer.BeforeSummaryRead = _ =>
        {
            if (++attempts == 1) throw new ReviewTransientException();
            return Task.CompletedTask;
        };
        await using var db = context.CreateDb(retry: true);
        var service = context.Service(db);
        if (operation == "delete") await service.DeleteAsync(context.UserIds[0], context.GameId);
        else await service.UpsertAsync(context.UserIds[0], context.GameId, 4.5m, null);
        Assert.Equal(2, attempts);
        Assert.Empty(db.ChangeTracker.Entries());
        var game = await context.GameAsync();
        Assert.Equal(operation == "delete" ? 0 : 1, game.RatingCount);
        Assert.Equal(operation == "delete" ? 0m : 4.5m, game.RatingSum);
        Assert.Equal(operation == "delete" ? 0 : 1, await db.Reviews.CountAsync());
    }

    [Fact]
    public async Task ActualCounterSql_UsesDatabaseArithmetic_NotReadModifyWrite()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(4.5m);
        await context.UpsertAsync(2m);
        await context.DeleteAsync();
        var statements = context.Observer.Commands.Where(sql => sql.StartsWith("UPDATE \"Games\"")).ToArray();
        Assert.Equal(3, statements.Length);
        Assert.Contains("\"RatingCount\" = \"g\".\"RatingCount\" + 1", statements[0]);
        Assert.Contains("ef_add(\"g\".\"RatingSum\",", statements[0]);
        Assert.DoesNotContain("SET \"RatingCount\"", statements[1]);
        Assert.Contains("ef_add(\"g\".\"RatingSum\",", statements[1]);
        Assert.Contains("\"RatingCount\" = \"g\".\"RatingCount\" - 1", statements[2]);
        Assert.Contains("ef_add(\"g\".\"RatingSum\",", statements[2]);
        Assert.Contains("\"g\".\"RatingCount\" > 0", statements[2]);
    }

    [Fact]
    public async Task CatalogRefresh_DoesNotOverwriteCountersChangedAfterGameWasLoaded()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb();
        var repository = new GameRepository(db);
        var stale = (await repository.FindAsync("fake", 42))!;
        Assert.Equal(0, stale.RatingCount);
        await context.UpsertAsync(4.5m);
        stale.Name = "Refreshed metadata";
        await repository.UpdateAsync(stale);
        var game = await context.GameAsync();
        Assert.Equal("Refreshed metadata", game.Name);
        Assert.Equal(1, game.RatingCount);
        Assert.Equal(4.5m, game.RatingSum);
        // Reads and writes through the review service always query the persisted totals.
        Assert.Equal(1, (await context.Service(db).GetSummaryAsync(context.GameId)).Count);
    }

    [Fact]
    public async Task LongLivedScope_UsesFreshReviewState_AndTracksNoReviewsOrGames()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb();
        var service = context.Service(db);
        await service.UpsertAsync(context.UserIds[0], context.GameId, 1m, null);
        await context.UpsertAsync(4.5m);
        Assert.Equal(4.5m, (await service.GetMineAsync(context.UserIds[0], context.GameId)).Review.Rating);
        var edited = await service.UpsertAsync(context.UserIds[0], context.GameId, 2m, null);
        Assert.Equal(2m, edited.RatingSummary.Average);
        Assert.Equal(2m, (await context.GameAsync()).RatingSum);
        await service.DeleteAsync(context.UserIds[0], context.GameId);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnrelatedInsertFailure_IsNotMistakenForUniqueRace()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb();
        await Assert.ThrowsAsync<DbUpdateException>(() => context.Service(db)
            .UpsertAsync(int.MaxValue, context.GameId, 3m, null));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.Reviews.ToListAsync());
        Assert.Equal(0, (await context.GameAsync()).RatingCount);
        Assert.Equal(0m, (await context.GameAsync()).RatingSum);
    }
}
