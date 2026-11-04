using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Playtrough.Api.Catalog;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public sealed class GameBrainPersistenceTests
{
    [Fact]
    public async Task RefreshPreservesLegacyIdentityAndMetadata_ExpiryRestoresLegacyWithoutLosingReviews()
    {
        using var context = new GamePersistenceTestContext();
        using var scope = context.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legacy = GamePersistenceTestContext.LocalGame(context.Clock.GetUtcNow().UtcDateTime, 7);
        legacy.Provider = "rawg";
        legacy.GameBrainId = 42;
        legacy.RatingCount = 2;
        legacy.RatingSum = 9;
        db.Games.Add(legacy);
        await db.SaveChangesAsync();
        var refreshed = await context.GetAsync(42, "gamebrain");
        Assert.Equal(legacy.Id, refreshed.Id);
        Assert.Equal(7, refreshed.ExternalId);
        Assert.Equal(42, refreshed.GameBrainId);
        Assert.Equal("rawg", refreshed.Provider);
        Assert.Equal("old-name", refreshed.Slug);
        Assert.NotNull(refreshed.LegacyMetadataJson);
        Assert.Equal(9, refreshed.RatingSum);
        context.Clock.UtcNow += TimeSpan.FromHours(24);
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GameBrainQuotaException());
        var expired = await context.GetAsync(42, "gamebrain");
        Assert.Equal("Old name", expired.Name);
        Assert.Null(expired.ProviderDetailsJson);
        Assert.Null(expired.MetadataExpiresAt);
        Assert.Equal(9, expired.RatingSum);
        Assert.Equal(legacy.Id, expired.Id);
    }

    [Fact]
    public async Task PersistedCacheDoesNotExtendAnOlderHttpResponseLifetime()
    {
        using var context = new GamePersistenceTestContext();
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GamePersistenceTestContext.Details() with { MetadataFetchedAt = context.Clock.UtcNow.AddHours(-23).UtcDateTime });
        var game = await context.GetAsync(42, "gamebrain");
        Assert.Equal(context.Clock.UtcNow.AddHours(1).UtcDateTime, game.MetadataExpiresAt);
        context.Clock.UtcNow += TimeSpan.FromHours(1);
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GameBrainQuotaException());
        var expired = await context.GetAsync(42, "gamebrain");
        Assert.Equal("Juego #42", expired.Name);
        Assert.Null(expired.CoverImageUrl);
        Assert.Null(expired.ProviderDetailsJson);
    }

    [Fact]
    public async Task Backfill_TriesIdentifiersInOrder_ReportsMissesAndSkipsSuccessfulRowsOnRerun()
    {
        using var context = new GamePersistenceTestContext();
        using var scope = context.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var game = GamePersistenceTestContext.LocalGame(context.Clock.UtcNow.UtcDateTime, 7);
        game.Provider = "rawg"; game.SteamId = 10; game.IgdbId = 20; game.FirstReleaseDate = new(2020, 1, 1);
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var seen = new List<GameMatchRequest>();
        context.Provider.Setup(provider => provider.MatchAsync(It.IsAny<GameMatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameMatchRequest request, CancellationToken _) => { seen.Add(request); return request.Name is null ? null : 42; });
        var command = new GameBrainBackfill(db, context.Provider.Object, new AdvancingCatalogTime());
        using var report = new StringWriter();
        Assert.Equal(0, await command.RunAsync(report));
        Assert.Equal(0, await command.RunAsync(report));
        Assert.Equal(3, seen.Count);
        Assert.Equal(10, seen[0].SteamId);
        Assert.Equal(20, seen[1].IgdbId);
        Assert.Equal(2020, seen[2].Year);
        Assert.Contains("\"status\":\"miss\"", report.ToString());
        Assert.Contains("\"status\":\"matched\"", report.ToString());
        Assert.Equal(42, (await db.Games.SingleAsync()).GameBrainId);
        Assert.Equal("Old name", (await db.Games.SingleAsync()).Name);
    }

    [Fact]
    public async Task Backfill_ReportsMissingYearAndCollision_AndStopsOnQuota()
    {
        using var context = new GamePersistenceTestContext();
        using var scope = context.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var id = 1; id <= 4; id++)
        {
            var game = GamePersistenceTestContext.LocalGame(context.Clock.UtcNow.UtcDateTime, id);
            game.Provider = "rawg";
            if (id == 1) game.GameBrainId = 42;
            if (id >= 3) game.SteamId = id;
            db.Games.Add(game);
        }
        await db.SaveChangesAsync();
        context.Provider.Setup(provider => provider.MatchAsync(It.Is<GameMatchRequest>(request => request.SteamId == 3), It.IsAny<CancellationToken>())).ReturnsAsync(42);
        context.Provider.Setup(provider => provider.MatchAsync(It.Is<GameMatchRequest>(request => request.SteamId == 4), It.IsAny<CancellationToken>())).ThrowsAsync(new GameBrainQuotaException());
        using var report = new StringWriter();
        Assert.Equal(2, await new GameBrainBackfill(db, context.Provider.Object, new AdvancingCatalogTime()).RunAsync(report));
        Assert.Contains("missing_identifiers_or_year", report.ToString());
        Assert.Contains("collision_requires_review", report.ToString());
        Assert.Contains("quota_exhausted_resume_later", report.ToString());
        Assert.Equal(1, await db.Games.CountAsync(game => game.GameBrainId != null));
    }
}
