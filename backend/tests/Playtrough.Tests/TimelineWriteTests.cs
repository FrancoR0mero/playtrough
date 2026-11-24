using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class TimelineWriteTests
{
    [Fact]
    public async Task LibraryLifecycle_RecordsOnlyAddedAndRealStatusChanges_AndRetainsHistoryAfterRemoval()
    {
        using var c = new LibraryTestContext();
        await c.AddAsync(LibraryStatus.Playing);
        c.Clock.UtcNow = c.Clock.UtcNow.AddHours(1);
        await c.ChangeAsync(LibraryStatus.Completed);
        await c.ChangeAsync(LibraryStatus.Completed);
        await c.RemoveAsync();
        await using var db = c.CreateDb();
        var entries = await db.TimelineEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(3, (await c.EventsAsync()).Count);
        Assert.Equal(TimelineEntryType.Added, entries[0].Type);
        Assert.Null(entries[0].FromStatus);
        Assert.Equal(LibraryStatus.Playing, entries[0].ToStatus);
        Assert.Equal(TimelineEntryType.StatusChanged, entries[1].Type);
        Assert.Equal(LibraryStatus.Playing, entries[1].FromStatus);
        Assert.Equal(LibraryStatus.Completed, entries[1].ToStatus);
        Assert.All(entries, e =>
        {
            Assert.True(e.Id > 0);
            Assert.NotNull(e.LibraryEventId);
            Assert.Null(e.ReviewId);
            Assert.False(e.IsHidden);
            Assert.Equal(c.UserId, e.UserId);
            Assert.Equal(c.GameId, e.GameId);
        });
        Assert.Null(await c.EntryAsync());
        await c.AddAsync(LibraryStatus.Abandoned);
        Assert.Equal(3, await db.TimelineEntries.CountAsync());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task LibraryDatesAndFeedEligibility_UseEffectiveDateAndEventTimestamp(int days, bool eligible)
    {
        using var c = new LibraryTestContext();
        var effective = DateOnly.FromDateTime(c.Clock.UtcNow.UtcDateTime).AddDays(-days);
        await c.AddAsync(date: effective);
        await using var db = c.CreateDb();
        var entry = await db.TimelineEntries.AsNoTracking().SingleAsync();
        var origin = Assert.Single(await c.EventsAsync());
        Assert.Equal(effective, entry.OccurredOn);
        Assert.Equal(origin.RecordedAt, entry.RecordedAt);
        Assert.Equal(c.Clock.UtcNow.UtcDateTime, entry.RecordedAt);
        Assert.Equal(DateTimeKind.Utc, entry.RecordedAt.Kind);
        Assert.Equal(eligible, entry.FeedEligible);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public async Task Writer_UsesConfiguredWindowAndStagesWithoutSaving(int window, bool eligible)
    {
        using var c = new LibraryTestContext();
        await using var db = c.CreateDb();
        var writer = new TimelineWriter(db, Options.Create(new FeedSettings { MaxBackdateDays = window }));
        var change = new LibraryEvent
        {
            UserId = c.UserId, GameId = c.GameId, Type = LibraryEventType.Added, ToStatus = LibraryStatus.Backlog,
            OccurredOn = DateOnly.FromDateTime(c.Clock.UtcNow.UtcDateTime).AddDays(-2), RecordedAt = c.Clock.UtcNow.UtcDateTime,
        };
        var entry = writer.AddLibraryEvent(change)!;
        Assert.Equal(eligible, entry.FeedEligible);
        Assert.Equal(EntityState.Added, db.Entry(entry).State);
        Assert.Empty(await db.TimelineEntries.AsNoTracking().ToListAsync());
        Assert.Empty(await c.EventsAsync());
        Assert.Null(writer.AddLibraryEvent(new LibraryEvent { Type = LibraryEventType.Removed }));
        await db.SaveChangesAsync();
        Assert.Equal(change.Id, entry.LibraryEventId);
    }

    [Theory]
    [InlineData(false, "LibraryEvents")]
    [InlineData(false, "TimelineEntries")]
    [InlineData(true, "LibraryEvents")]
    [InlineData(true, "TimelineEntries")]
    public async Task LibraryFailure_RollsBackOriginAndTimeline_WithoutLeakingTrackedEntries(bool changeStatus, string failedTable)
    {
        using var c = new LibraryTestContext();
        if (changeStatus) await c.AddAsync();
        await using var db = c.CreateDb();
        await db.Database.ExecuteSqlRawAsync(FailInsert(failedTable));
        var service = c.Service(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => changeStatus
            ? service.ChangeStatusAsync(c.UserId, c.GameId, LibraryStatus.Completed)
            : service.AddAsync(c.UserId, c.GameId, LibraryStatus.Playing));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(changeStatus ? 1 : 0, await db.LibraryEvents.CountAsync());
        Assert.Equal(changeStatus ? 1 : 0, await db.TimelineEntries.CountAsync());
        if (changeStatus) Assert.Equal(LibraryStatus.Backlog, (await c.EntryAsync())!.Status);
        else Assert.Null(await c.EntryAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_timeline_test;");
        Assert.Equal(0, await db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReviewLifecycle_OneStableEntryAcrossEdits_DeletionRemovesEntry()
    {
        using var c = new ReviewTestContext();
        var review = (await c.UpsertAsync(4, "first text")).Review;
        await using var db = c.CreateDb();
        var original = await db.TimelineEntries.AsNoTracking().SingleAsync();
        Assert.Equal(review.Id, original.ReviewId);
        Assert.Equal(TimelineEntryType.Reviewed, original.Type);
        Assert.Equal(DateOnly.FromDateTime(review.CreatedAt), original.OccurredOn);
        Assert.Equal(review.CreatedAt, original.RecordedAt);
        Assert.True(original.FeedEligible);
        Assert.Null(original.LibraryEventId);
        Assert.Null(original.FromStatus);
        Assert.Null(original.ToStatus);
        c.Clock.UtcNow = c.Clock.UtcNow.AddDays(2);
        await c.UpsertAsync(5, "updated text");
        await c.UpsertAsync(5, "updated text");
        Assert.Equal(original.Id, (await db.TimelineEntries.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(original.RecordedAt, (await db.TimelineEntries.AsNoTracking().SingleAsync()).RecordedAt);
        await c.DeleteAsync();
        Assert.Empty(await db.TimelineEntries.ToListAsync());
    }

    [Theory]
    [InlineData("Reviews")]
    [InlineData("TimelineEntries")]
    public async Task ReviewCreateFailure_RollsBackReviewTimelineAndRatingCounters(string failedTable)
    {
        using var c = new ReviewTestContext();
        await using var db = c.CreateDb();
        await db.Database.ExecuteSqlRawAsync(FailInsert(failedTable));
        await Assert.ThrowsAsync<DbUpdateException>(() => c.Service(db).UpsertAsync(c.UserIds[0], c.GameId, 4, "text"));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.Reviews.ToListAsync());
        Assert.Empty(await db.TimelineEntries.ToListAsync());
        Assert.Equal(0, (await c.GameAsync()).RatingCount);
        Assert.Equal(0, (await c.GameAsync()).RatingSum);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_timeline_test;");
        Assert.Equal(0, await db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReviewDeleteFailure_RestoresTimelineReviewAndRatingCounters()
    {
        using var c = new ReviewTestContext();
        await c.UpsertAsync(4);
        await using var db = c.CreateDb();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_timeline_test BEFORE DELETE ON Reviews BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => c.Service(db).DeleteAsync(c.UserIds[0], c.GameId));
        Assert.Single(await db.Reviews.ToListAsync());
        Assert.Single(await db.TimelineEntries.ToListAsync());
        Assert.Equal(1, (await c.GameAsync()).RatingCount);
        Assert.Equal(4, (await c.GameAsync()).RatingSum);
    }

    [Fact]
    public async Task SourceIndexesRejectDuplicates_AndReviewCascadeIsEnforcedOutsideService()
    {
        using var library = new LibraryTestContext();
        await library.AddAsync();
        await using (var db = library.CreateDb())
        {
            var source = await db.LibraryEvents.AsNoTracking().SingleAsync();
            var writer = new TimelineWriter(db, Options.Create(new FeedSettings()));
            writer.AddLibraryEvent(source);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        using var reviews = new ReviewTestContext();
        await reviews.UpsertAsync();
        await using (var db = reviews.CreateDb())
        {
            var source = await db.Reviews.AsNoTracking().SingleAsync();
            new TimelineWriter(db, Options.Create(new FeedSettings())).AddReview(source);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = reviews.CreateDb())
        {
            await db.Reviews.ExecuteDeleteAsync();
            Assert.Empty(await db.TimelineEntries.ToListAsync());
        }
    }

    [Fact]
    public async Task LibraryConcurrentChanges_CreateOnlyEntriesForWinningEvents()
    {
        using var c = new LibraryTestContext();
        await c.AddAsync();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        c.Observer.BeforeSave = async (_, ct) =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };
        await Task.WhenAll(c.ChangeAsync(LibraryStatus.Playing), c.ChangeAsync(LibraryStatus.Playing));
        await using var db = c.CreateDb();
        var events = await c.EventsAsync();
        var entries = await db.TimelineEntries.AsNoTracking().ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(events.Select(e => (int?)e.Id).Order(), entries.Select(e => e.LibraryEventId).Order());
    }

    [Fact]
    public async Task AccountDeletion_CascadesEventsAndTimelineWithoutDeletingTheGame()
    {
        using var c = new LibraryTestContext();
        await c.AddAsync();
        await using var db = c.CreateDb();
        await db.Users.Where(u => u.Id == c.UserId).ExecuteDeleteAsync();
        Assert.Empty(await db.TimelineEntries.ToListAsync());
        Assert.Empty(await db.LibraryEvents.ToListAsync());
        Assert.Empty(await db.LibraryEntries.ToListAsync());
        Assert.True(await db.Games.AnyAsync(g => g.Id == c.GameId));
    }

    [Fact]
    public async Task ReviewConcurrentCreates_ProduceExactlyOneTimelineEntry()
    {
        using var c = new ReviewTestContext();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        c.Observer.AfterReviewRead = async ct =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };
        await Task.WhenAll(Task.Run(() => c.UpsertAsync(3)), Task.Run(() => c.UpsertAsync(4)));
        await using var db = c.CreateDb();
        Assert.Equal((await db.Reviews.SingleAsync()).Id, (await db.TimelineEntries.SingleAsync()).ReviewId);
    }

    [Fact]
    public async Task ReviewExecutionStrategyRetry_DoesNotLeakRolledBackTimelineEntries()
    {
        using var c = new ReviewTestContext();
        var attempts = 0;
        c.Observer.BeforeSummaryRead = _ =>
        {
            if (++attempts == 1) throw new ReviewTransientException();
            return Task.CompletedTask;
        };
        await using var db = c.CreateDb(retry: true);
        await c.Service(db).UpsertAsync(c.UserIds[0], c.GameId, 4, null);
        Assert.Equal(2, attempts);
        Assert.Single(await db.TimelineEntries.AsNoTracking().ToListAsync());
        Assert.Single(await db.Reviews.AsNoTracking().ToListAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static string FailInsert(string table) => table switch
    {
        "Reviews" => "CREATE TRIGGER fail_timeline_test BEFORE INSERT ON Reviews BEGIN SELECT RAISE(ABORT, 'test failure'); END;",
        "LibraryEvents" => "CREATE TRIGGER fail_timeline_test BEFORE INSERT ON LibraryEvents BEGIN SELECT RAISE(ABORT, 'test failure'); END;",
        "TimelineEntries" => "CREATE TRIGGER fail_timeline_test BEFORE INSERT ON TimelineEntries BEGIN SELECT RAISE(ABORT, 'test failure'); END;",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };
}
