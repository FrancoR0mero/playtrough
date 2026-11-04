using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class GameRepositoryTests
{
    [Fact]
    public async Task UniqueIndex_RejectsSameProviderAndExternalId_ButAllowsOtherCompositeKeys()
    {
        using var context = new GamePersistenceTestContext();
        await context.SeedAsync(TimeSpan.Zero);
        using var scope = context.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicate = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime);
        db.Games.Add(duplicate);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        db.Entry(duplicate).State = EntityState.Detached;
        var otherProvider = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime);
        otherProvider.Provider = "other-provider";
        var otherId = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime, 99);
        db.Games.AddRange(otherProvider, otherId);
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.Games.CountAsync());
    }

    [Fact]
    public async Task RecoveringDuplicateInsert_DetachesCandidate_AndContextCanStillSave()
    {
        using var context = new GamePersistenceTestContext();
        var winner = await context.SeedAsync(TimeSpan.Zero);
        using var scope = context.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var candidate = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime);
        var existing = await repository.InsertOrGetExistingAsync(candidate);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(winner.Id, existing.Id);
        Assert.Equal(EntityState.Detached, db.Entry(candidate).State);
        Assert.Equal(1, context.InsertFailures.UniqueConflicts);
        Assert.Equal(0, await db.SaveChangesAsync());
        Assert.Single(await context.ReadAsync());
    }

    [Fact]
    public async Task NonRaceInsertFailure_IsNotSwallowed_AndFailedCandidateIsDetached()
    {
        using var context = new GamePersistenceTestContext();
        using var scope = context.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var candidate = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime);
        candidate.Name = null!; // Violates a real NOT NULL constraint, not the unique index.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => repository.InsertOrGetExistingAsync(candidate));
        Assert.Equal(1299, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(EntityState.Detached, db.Entry(candidate).State);
        Assert.Empty(await context.ReadAsync());
    }

    [Fact]
    public async Task Lists_AreStoredAsJson_AndInPlaceEditsAreTrackedForAllThreeColumns()
    {
        using var context = new GamePersistenceTestContext();
        await context.GetAsync();
        using (var scope = context.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var game = await db.Games.SingleAsync();
            game.Platforms.Add("Plataforma \"nueva\" & ñ");
            game.Genres.Clear();
            game.Developers[0] = "Estudio renovado";
            await db.SaveChangesAsync();
        }
        var stored = Assert.Single(await context.ReadAsync());
        Assert.Equal("Plataforma \"nueva\" & ñ", stored.Platforms[^1]);
        Assert.Empty(stored.Genres);
        Assert.Equal(["Estudio renovado"], stored.Developers);
        using var readScope = context.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await readDb.Database.OpenConnectionAsync();
        await using var command = readDb.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Platforms, Genres, Developers FROM Games;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for (var index = 0; index < 3; index++)
        {
            using var json = JsonDocument.Parse(reader.GetString(index));
            Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        }
    }

    [Fact]
    public void OfflineFactory_UsesMySqlModel_AndGeneratesOnlyGamesMigrationWithoutOpeningConnection()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        var game = db.Model.FindEntityType(typeof(Game))!;
        Assert.Equal("json", game.FindProperty(nameof(Game.Platforms))!.GetColumnType());
        Assert.Equal("json", game.FindProperty(nameof(Game.Genres))!.GetColumnType());
        Assert.Equal("json", game.FindProperty(nameof(Game.Developers))!.GetColumnType());
        Assert.Equal("date", game.FindProperty(nameof(Game.FirstReleaseDate))!.GetColumnType());
        Assert.True(Assert.Single(game.GetIndexes()).IsUnique);
        var script = db.GetService<IMigrator>().GenerateScript("20261018070349_AddRoleChangeLogs", "20261025065759_AddGames");
        Assert.Contains("CREATE TABLE `Games`", script);
        Assert.Contains("CREATE UNIQUE INDEX `IX_Games_Provider_ExternalId`", script);
        Assert.Contains("`FirstReleaseDate` date NULL", script);
        Assert.DoesNotContain("ALTER TABLE", script);
        Assert.DoesNotContain("CREATE TABLE `Users`", script);
        Assert.DoesNotContain("INSERT INTO `Games`", script);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void NormalDesignFactory_UsesExplicitConfigurationWithoutValidatingUnrelatedOptionsOrRunningSeed()
    {
        using var db = new AppDbContextFactory().CreateDbContext([
            "--environment", "Testing",
            "--ConnectionStrings:DefaultConnection", "Server=design.example.invalid;Database=design_tests;User ID=design_user;",
            "--Jwt:Key", "test-only-invalid-jwt", "--Catalog:UseFake", "true",
        ]);
        var connection = db.Database.GetDbConnection();
        Assert.Equal("design_tests", connection.Database);
        Assert.Equal("design.example.invalid", connection.DataSource);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void NormalDesignFactory_InvalidConnectionReportsOnlySafeValidationMessage()
    {
        var error = Assert.Throws<OptionsValidationException>(() => new AppDbContextFactory().CreateDbContext([
            "--environment", "Testing",
            "--ConnectionStrings:DefaultConnection", "Server=localhost;Port=test-only-db-canary;Password=test-only-db-canary;",
        ]));
        Assert.Contains("ConnectionStrings:DefaultConnection", error.Message);
        Assert.DoesNotContain("test-only-db-canary", error.ToString());
        Assert.Null(error.InnerException);
    }
}
