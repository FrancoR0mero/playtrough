using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class LibraryServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_CreatesEntryAndExactlyOneEvent_WithEffectiveDateAndRealUtcTimestamps(bool retroactive)
    {
        using var context = new LibraryTestContext();
        DateOnly? date = retroactive ? new(2020, 2, 29) : null;
        var added = await context.AddAsync(LibraryStatus.Playing, date);
        var stored = (await context.EntryAsync())!;
        var change = Assert.Single(await context.EventsAsync());
        var expectedDate = date ?? DateOnly.FromDateTime(context.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(added.Id, stored.Id);
        Assert.Equal(context.UserId, stored.UserId);
        Assert.Equal(context.GameId, stored.GameId);
        Assert.Equal(LibraryStatus.Playing, stored.Status);
        Assert.Equal(expectedDate, stored.StatusChangedOn);
        Assert.NotEqual(Guid.Empty, stored.ConcurrencyToken);
        Assert.Equal(context.Clock.GetUtcNow().UtcDateTime, stored.AddedAt);
        Assert.Equal(DateTimeKind.Utc, stored.AddedAt.Kind);
        Assert.Equal(context.UserId, change.UserId);
        Assert.Equal(context.GameId, change.GameId);
        Assert.Equal(LibraryEventType.Added, change.Type);
        Assert.Null(change.FromStatus);
        Assert.Equal(LibraryStatus.Playing, change.ToStatus);
        Assert.Equal(expectedDate, change.OccurredOn);
        Assert.Equal(stored.AddedAt, change.RecordedAt);
        Assert.Equal(DateTimeKind.Utc, change.RecordedAt.Kind);
        Assert.Equal(1, context.Observer.Attempts);
    }

    [Fact]
    public async Task Duplicate_ReturnsAlreadyInLibrary_WithoutSavingAgain()
    {
        using var context = new LibraryTestContext();
        var added = await context.AddAsync();
        await Error("already_in_library", () => context.AddAsync(LibraryStatus.Completed));
        Assert.Equal(added.Id, (await context.EntryAsync())!.Id);
        Assert.Single(await context.EventsAsync());
        Assert.Equal(1, context.Observer.Attempts);
    }

    [Fact]
    public async Task ConcurrentAdds_UniqueIndexKeepsOneEntryAndOneEvent_AndLoserIsControlled()
    {
        using var context = new LibraryTestContext();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        context.Observer.BeforeSave = async (_, ct) =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };
        async Task<string> Add()
        {
            try { await context.AddAsync(); return "ok"; }
            catch (LibraryException exception) { return exception.Code; }
        }
        var results = await Task.WhenAll(Add(), Add());
        Assert.Single(results, result => result == "ok");
        Assert.Single(results, result => result == "already_in_library");
        Assert.Equal(1, context.Observer.UniqueConflicts);
        await using var db = context.CreateDb();
        Assert.Single(await db.LibraryEntries.ToListAsync());
        Assert.Single(await context.EventsAsync());
    }

    [Fact]
    public async Task MissingLocalGame_IsGameNotFound_WithoutWrites()
    {
        using var context = new LibraryTestContext();
        await using var db = context.CreateDb();
        await Error("game_not_found", () => context.Service(db).AddAsync(context.UserId, int.MaxValue, LibraryStatus.Backlog));
        Assert.Empty(await db.LibraryEntries.ToListAsync());
        Assert.Empty(await context.EventsAsync());
        Assert.Equal(0, context.Observer.Attempts);
    }

    [Theory]
    [InlineData(LibraryStatus.Playing)]
    [InlineData(LibraryStatus.Backlog)]
    [InlineData(LibraryStatus.Beaten)]
    [InlineData(LibraryStatus.Completed)]
    public async Task AllTransitionsAreAllowed_AndRecordOldAndNewStatuses(LibraryStatus from)
    {
        using var context = new LibraryTestContext();
        var added = await context.AddAsync(from, new DateOnly(2020, 1, 1));
        var previous = from;
        var token = added.ConcurrencyToken;
        foreach (var target in Enum.GetValues<LibraryStatus>().Where(status => status != from))
        {
            var updated = await context.ChangeAsync(target, new DateOnly(2020, 1, 2));
            Assert.Equal(target, updated.Status);
            Assert.Equal(new DateOnly(2020, 1, 2), updated.StatusChangedOn);
            Assert.Equal(added.AddedAt, updated.AddedAt);
            Assert.Equal(added.Id, updated.Id);
            Assert.NotEqual(token, updated.ConcurrencyToken);
            var change = (await context.EventsAsync()).Last();
            Assert.Equal(LibraryEventType.StatusChanged, change.Type);
            Assert.Equal(previous, change.FromStatus);
            Assert.Equal(target, change.ToStatus);
            Assert.Equal(updated.StatusChangedOn, change.OccurredOn);
            previous = target;
            token = updated.ConcurrencyToken;
        }
        // Return to the initial status too; no transition has special restrictions.
        await context.ChangeAsync(from, new DateOnly(2020, 1, 2));
        Assert.Equal(5, (await context.EventsAsync()).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1969-12-31")]
    [InlineData("2020-01-01")]
    [InlineData("2099-01-01")]
    public async Task SameStatus_IsIdempotent_EvenWithDifferentEffectiveDate(string? date)
    {
        using var context = new LibraryTestContext();
        var added = await context.AddAsync(LibraryStatus.Playing);
        context.Clock.UtcNow += TimeSpan.FromHours(2);
        await context.ChangeAsync(LibraryStatus.Playing, date is null ? null : DateOnly.Parse(date));
        var stored = (await context.EntryAsync())!;
        Assert.Equal(added.Id, stored.Id);
        Assert.Equal(added.Status, stored.Status);
        Assert.Equal(added.AddedAt, stored.AddedAt);
        Assert.Equal(added.StatusChangedOn, stored.StatusChangedOn);
        Assert.Equal(added.ConcurrencyToken, stored.ConcurrencyToken);
        Assert.Single(await context.EventsAsync());
        Assert.Equal(1, context.Observer.Attempts);
    }

    [Theory]
    [InlineData("1969-12-31")]
    [InlineData("2026-10-29")]
    public async Task InvalidDate_IsRejectedOnAddAndChange_WithoutWrites(string date)
    {
        using var context = new LibraryTestContext();
        await Error("invalid_date", () => context.AddAsync(date: DateOnly.Parse(date)));
        Assert.Null(await context.EntryAsync());
        Assert.Empty(await context.EventsAsync());
        var entry = await context.AddAsync();
        await Error("invalid_date", () => context.ChangeAsync(LibraryStatus.Completed, DateOnly.Parse(date)));
        Assert.Equal(entry.ConcurrencyToken, (await context.EntryAsync())!.ConcurrencyToken);
        Assert.Single(await context.EventsAsync());
    }

    [Theory]
    [InlineData("1970-01-01")]
    [InlineData("2026-10-28")]
    public async Task MinimumAndTimezoneToleranceDates_AreAccepted(string date)
    {
        using var context = new LibraryTestContext();
        var effective = DateOnly.Parse(date);
        await context.AddAsync(date: effective);
        await context.ChangeAsync(LibraryStatus.Completed, effective);
        Assert.All(await context.EventsAsync(), change => Assert.Equal(effective, change.OccurredOn));
    }

    [Fact]
    public async Task ChangeBeforePreviousDate_ReportsMinimumDate_AndDoesNotChangeAnything()
    {
        using var context = new LibraryTestContext();
        var minimum = new DateOnly(2020, 5, 10);
        var entry = await context.AddAsync(date: minimum);
        var error = await Error("date_before_previous_event", () => context.ChangeAsync(LibraryStatus.Beaten, minimum.AddDays(-1)));
        Assert.Equal(minimum, error.MinDate);
        Assert.Equal(entry.ConcurrencyToken, (await context.EntryAsync())!.ConcurrencyToken);
        Assert.Single(await context.EventsAsync());
    }

    [Fact]
    public async Task DefaultChangeDate_IsUtcToday_AndComparedWithPreviousDate()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync(date: new DateOnly(2026, 10, 28));
        var error = await Error("date_before_previous_event", () => context.ChangeAsync(LibraryStatus.Playing));
        Assert.Equal(new DateOnly(2026, 10, 28), error.MinDate);
        context.Clock.UtcNow += TimeSpan.FromDays(1);
        var changed = await context.ChangeAsync(LibraryStatus.Playing);
        Assert.Equal(new DateOnly(2026, 10, 28), changed.StatusChangedOn);
        Assert.Equal(changed.StatusChangedOn, (await context.EventsAsync()).Last().OccurredOn);
    }

    [Fact]
    public async Task Remove_PreservesHistory_AndReaddCreatesNewIdentityAndAddedEvent()
    {
        using var context = new LibraryTestContext();
        var original = await context.AddAsync(date: new DateOnly(2020, 1, 1));
        await context.ChangeAsync(LibraryStatus.Beaten, new DateOnly(2020, 1, 2));
        await context.RemoveAsync();
        Assert.Null(await context.EntryAsync());
        var events = await context.EventsAsync();
        Assert.Equal(3, events.Count);
        var removed = events.Last();
        Assert.Equal(LibraryEventType.Removed, removed.Type);
        Assert.Equal(LibraryStatus.Beaten, removed.FromStatus);
        Assert.Null(removed.ToStatus);
        Assert.Equal(DateOnly.FromDateTime(context.Clock.GetUtcNow().UtcDateTime), removed.OccurredOn);
        Assert.Equal(context.Clock.GetUtcNow().UtcDateTime, removed.RecordedAt);
        // A new library lifecycle may start earlier than a previous lifecycle's events.
        var readded = await context.AddAsync(LibraryStatus.Playing, new DateOnly(2010, 1, 1));
        Assert.NotEqual(original.Id, readded.Id);
        Assert.NotEqual(original.ConcurrencyToken, readded.ConcurrencyToken);
        events = await context.EventsAsync();
        Assert.Equal(4, events.Count);
        Assert.Equal(LibraryEventType.Added, events.Last().Type);
        Assert.Null(events.Last().FromStatus);
        Assert.Equal(LibraryStatus.Playing, events.Last().ToStatus);
    }

    [Fact]
    public async Task MissingEntry_ChangeAndRemoveAreNotInLibrary_AndGetReturnsNull()
    {
        using var context = new LibraryTestContext();
        Assert.Null(await context.EntryAsync());
        await Error("not_in_library", () => context.ChangeAsync(LibraryStatus.Playing));
        await Error("not_in_library", () => context.RemoveAsync());
        Assert.Empty(await context.EventsAsync());
    }

    [Theory]
    [InlineData("add")]
    [InlineData("change")]
    [InlineData("remove")]
    public async Task EventSaveFailure_RollsBackEntireMutation_AndDoesNotLeakPendingChanges(string operation)
    {
        using var context = new LibraryTestContext();
        LibraryEntry? original = operation == "add" ? null : await context.AddAsync();
        await context.FailEventWritesAsync();
        await using var db = context.CreateDb();
        var service = context.Service(db);
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            switch (operation)
            {
                case "add": await service.AddAsync(context.UserId, context.GameId, LibraryStatus.Playing); break;
                case "change": await service.ChangeStatusAsync(context.UserId, context.GameId, LibraryStatus.Completed); break;
                case "remove": await service.RemoveAsync(context.UserId, context.GameId); break;
            }
        });
        var stored = await context.EntryAsync();
        if (original is null) Assert.Null(stored);
        else
        {
            Assert.Equal(original.Status, stored!.Status);
            Assert.Equal(original.ConcurrencyToken, stored.ConcurrencyToken);
            Assert.Equal(original.StatusChangedOn, stored.StatusChangedOn);
        }
        Assert.Equal(original is null ? 0 : 1, (await context.EventsAsync()).Count);
        Assert.Empty(db.ChangeTracker.Entries());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_library_event;");
        Assert.Equal(0, await db.SaveChangesAsync());
        Assert.Equal(original is null ? 0 : 1, (await context.EventsAsync()).Count);
    }

    [Fact]
    public async Task UsersAreIsolated_AndCanIndependentlyOwnTheSameGame()
    {
        using var context = new LibraryTestContext();
        var a = await context.AddAsync();
        Assert.Null(await context.EntryAsync(context.OtherUserId));
        await Error("not_in_library", () => context.ChangeAsync(LibraryStatus.Completed, userId: context.OtherUserId));
        await Error("not_in_library", () => context.RemoveAsync(context.OtherUserId));
        Assert.Equal(a.ConcurrencyToken, (await context.EntryAsync())!.ConcurrencyToken);
        var b = await context.AddAsync(LibraryStatus.Completed, userId: context.OtherUserId);
        Assert.NotEqual(a.Id, b.Id);
        await context.RemoveAsync(context.OtherUserId);
        Assert.Equal(a.ConcurrencyToken, (await context.EntryAsync())!.ConcurrencyToken);
        Assert.Null(await context.EntryAsync(context.OtherUserId));
        var events = await context.EventsAsync();
        Assert.Single(events, change => change.UserId == context.UserId);
        Assert.Equal(2, events.Count(change => change.UserId == context.OtherUserId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public async Task UndefinedEnum_IsInvalidStatus_OnAddAndChange(int value)
    {
        using var context = new LibraryTestContext();
        await Error("invalid_status", () => context.AddAsync((LibraryStatus)value));
        var entry = await context.AddAsync();
        await Error("invalid_status", () => context.ChangeAsync((LibraryStatus)value));
        Assert.Equal(entry.ConcurrencyToken, (await context.EntryAsync())!.ConcurrencyToken);
        Assert.Single(await context.EventsAsync());
    }

    private static async Task<LibraryException> Error(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<LibraryException>(action);
        Assert.Equal(code, error.Code);
        return error;
    }
}
