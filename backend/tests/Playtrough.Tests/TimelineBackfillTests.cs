using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class TimelineBackfillTests
{
    [Fact]
    public async Task Backfill_IsBatchedAndIdempotent_PreservesExistingEntries_AndSkipsRemovals()
    {
        using var c = new ReviewTestContext();
        await c.UpsertAsync(4, "already present");
        await using var db = c.CreateDb();
        var original = await db.TimelineEntries.AsNoTracking().SingleAsync();
        await db.TimelineEntries.Where(e => e.Id == original.Id).ExecuteUpdateAsync(u => u.SetProperty(e => e.IsHidden, true));
        var total = TimelineBackfillService.BatchSize + 1;
        for (var i = 0; i < total; i++)
        {
            db.LibraryEvents.Add(new LibraryEvent
            {
                UserId = c.UserIds[0], GameId = c.GameId,
                Type = i % 2 == 0 ? LibraryEventType.Added : LibraryEventType.StatusChanged,
                FromStatus = i % 2 == 0 ? null : LibraryStatus.Playing, ToStatus = LibraryStatus.Completed,
                OccurredOn = DateOnly.FromDateTime(c.Clock.UtcNow.UtcDateTime).AddDays(-8),
                RecordedAt = c.Clock.UtcNow.UtcDateTime,
            });
            var game = GamePersistenceTestContext.LocalGame(c.Clock.UtcNow.UtcDateTime, 1000 + i);
            db.Games.Add(game);
            await db.SaveChangesAsync();
            db.Reviews.Add(new Review
            {
                UserId = c.UserIds[0], GameId = game.Id, Rating = 3,
                CreatedAt = c.Clock.UtcNow.AddDays(-30).UtcDateTime, ConcurrencyToken = Guid.NewGuid(),
            });
        }
        db.LibraryEvents.Add(new LibraryEvent
        {
            UserId = c.UserIds[0], GameId = c.GameId, Type = LibraryEventType.Removed,
            OccurredOn = DateOnly.FromDateTime(c.Clock.UtcNow.UtcDateTime), RecordedAt = c.Clock.UtcNow.UtcDateTime,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = Service(db);
        Assert.Equal(new TimelineBackfillResult(total, total), await service.RunAsync());
        Assert.Equal(new TimelineBackfillResult(0, 0), await service.RunAsync());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(total * 2 + 1, await db.TimelineEntries.CountAsync());
        Assert.True((await db.TimelineEntries.AsNoTracking().SingleAsync(e => e.Id == original.Id)).IsHidden);
        Assert.Equal(total, await db.TimelineEntries.CountAsync(e => e.LibraryEventId != null && !e.FeedEligible));
        Assert.Equal(total + 1, await db.TimelineEntries.CountAsync(e => e.ReviewId != null && e.FeedEligible));
        Assert.DoesNotContain(await db.TimelineEntries.AsNoTracking().Include(e => e.LibraryEvent).ToListAsync(),
            e => e.LibraryEvent?.Type == LibraryEventType.Removed);
    }

    [Fact]
    public async Task Backfill_FailedBatchRollsBackAndCanBeRetried()
    {
        using var c = new LibraryTestContext();
        await c.AddAsync();
        await using var db = c.CreateDb();
        await db.TimelineEntries.ExecuteDeleteAsync(); // Recreate a pre-timeline event.
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_backfill BEFORE INSERT ON TimelineEntries BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).RunAsync());
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Single(await db.LibraryEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await db.TimelineEntries.AsNoTracking().ToListAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_backfill;");
        Assert.Equal(new TimelineBackfillResult(1, 0), await Service(db).RunAsync());
    }

    [Theory]
    [InlineData("User", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task BackfillEndpoint_IsAdminOnly_AndReturnsCreatedCounts(string role, HttpStatusCode expected)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync(role);
        var game = await ReviewEndpointsTests.OpenGameAsync(client);
        (await client.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/games/{game.Id}/reviews/me", new { rating = 4 })).EnsureSuccessStatusCode();
        using (var scope = c.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().TimelineEntries.ExecuteDeleteAsync();
        using var response = await client.PostAsync("/api/admin/timeline/backfill", null);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(new TimelineBackfillResult(1, 1), await response.Content.ReadFromJsonAsync<TimelineBackfillResult>());
            using var repeated = await client.PostAsync("/api/admin/timeline/backfill", null);
            repeated.EnsureSuccessStatusCode();
            Assert.Equal(new TimelineBackfillResult(0, 0), await repeated.Content.ReadFromJsonAsync<TimelineBackfillResult>());
        }
        else
        {
            using var scope = c.Factory.Services.CreateScope();
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().TimelineEntries.ToListAsync());
        }
    }

    [Fact]
    public async Task AnonymousBackfill_Is401()
    {
        using var c = new GamesApiTestContext();
        using var client = c.Factory.CreateClient();
        using var response = await client.PostAsync("/api/admin/timeline/backfill", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentBackfills_CreateEachMissingEntryOnce()
    {
        using var c = new LibraryTestContext();
        await c.AddAsync();
        await using var db = c.CreateDb();
        await db.TimelineEntries.ExecuteDeleteAsync();
        async Task<TimelineBackfillResult> Run()
        {
            await using var other = c.CreateDb();
            var result = await Service(other).RunAsync();
            Assert.Empty(other.ChangeTracker.Entries());
            return result;
        }
        var results = await Task.WhenAll(Task.Run(Run), Task.Run(Run));
        Assert.Equal(1, results.Sum(result => result.CreatedFromLibrary));
        Assert.All(results, result => Assert.Equal(0, result.CreatedFromReviews));
        Assert.Single(await db.TimelineEntries.AsNoTracking().ToListAsync());
    }

    private static TimelineBackfillService Service(AppDbContext db)
        => new(db, new TimelineWriter(db, Options.Create(new FeedSettings())), NullLogger<TimelineBackfillService>.Instance);

}
