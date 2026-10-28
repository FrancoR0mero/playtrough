using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class LibraryConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStatusChanges_RetryFromFreshState_WithoutOrphanEvents(bool sameTarget)
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        context.Observer.BeforeSave = async (_, ct) =>
        {
            if (Interlocked.Increment(ref arrivals) == 2) barrier.TrySetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };
        await Task.WhenAll(context.ChangeAsync(LibraryStatus.Playing),
            context.ChangeAsync(sameTarget ? LibraryStatus.Playing : LibraryStatus.Completed));
        var events = await context.EventsAsync();
        Assert.Equal(sameTarget ? 2 : 3, events.Count);
        var previous = LibraryStatus.Backlog;
        foreach (var change in events.Skip(1))
        {
            Assert.Equal(previous, change.FromStatus);
            Assert.NotEqual(change.FromStatus, change.ToStatus);
            previous = change.ToStatus!.Value;
        }
        Assert.Equal(previous, (await context.EntryAsync())!.Status);
        Assert.Equal(sameTarget ? 3 : 4, context.Observer.Attempts); // The fresh read may make the retry idempotent.
    }

    [Fact]
    public async Task FirstConflict_RereadsEntry_AndCreatesEventFromWinningStatus()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        context.Observer.BeforeSave = async (_, _) =>
        {
            context.Observer.BeforeSave = null;
            await context.ChangeAsync(LibraryStatus.Beaten, observe: false);
        };
        var result = await context.ChangeAsync(LibraryStatus.Completed);
        Assert.Equal(LibraryStatus.Completed, result.Status);
        var events = await context.EventsAsync();
        Assert.Equal(3, events.Count);
        Assert.Equal(LibraryStatus.Beaten, events.Last().FromStatus);
        Assert.Equal(LibraryStatus.Completed, events.Last().ToStatus);
        Assert.Equal(3, context.Observer.Attempts);
    }

    [Fact]
    public async Task Retry_RevalidatesDateAgainstWinner_AndReportsNewMinimum()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync(date: new DateOnly(2020, 1, 1));
        context.Observer.BeforeSave = async (_, _) =>
        {
            context.Observer.BeforeSave = null;
            await context.ChangeAsync(LibraryStatus.Beaten, new DateOnly(2020, 1, 15), observe: false);
        };
        var error = await Assert.ThrowsAsync<LibraryException>(() => context.ChangeAsync(LibraryStatus.Completed, new DateOnly(2020, 1, 10)));
        Assert.Equal("date_before_previous_event", error.Code);
        Assert.Equal(new DateOnly(2020, 1, 15), error.MinDate);
        Assert.Equal(LibraryStatus.Beaten, (await context.EntryAsync())!.Status);
        Assert.Equal(2, (await context.EventsAsync()).Count);
    }

    [Fact]
    public async Task Retry_WhenWinnerHasRequestedStatus_IsIdempotent()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync(date: new DateOnly(2020, 1, 1));
        LibraryEntry? winner = null;
        context.Observer.BeforeSave = async (_, _) =>
        {
            context.Observer.BeforeSave = null;
            winner = await context.ChangeAsync(LibraryStatus.Completed, new DateOnly(2020, 1, 15), observe: false);
        };
        var result = await context.ChangeAsync(LibraryStatus.Completed, new DateOnly(2020, 1, 10));
        Assert.Equal(winner!.ConcurrencyToken, result.ConcurrencyToken);
        Assert.Equal(winner.StatusChangedOn, result.StatusChangedOn);
        Assert.Equal(2, (await context.EventsAsync()).Count);
        Assert.Equal(2, context.Observer.Attempts);
    }

    [Fact]
    public async Task TwoConflicts_ReportConcurrentModification_WithoutThirdAttemptOrLeakedEvents()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        var conflicts = 0;
        context.Observer.BeforeSave = async (_, _) =>
        {
            await context.ChangeAsync(++conflicts == 1 ? LibraryStatus.Playing : LibraryStatus.Beaten, observe: false);
        };
        var error = await Assert.ThrowsAsync<LibraryException>(() => context.ChangeAsync(LibraryStatus.Completed));
        Assert.Equal("concurrent_modification", error.Code);
        Assert.Equal(2, conflicts);
        Assert.Equal(3, context.Observer.Attempts);
        Assert.Equal(LibraryStatus.Beaten, (await context.EntryAsync())!.Status);
        var events = await context.EventsAsync();
        Assert.Equal(3, events.Count);
        Assert.DoesNotContain(events, change => change.ToStatus == LibraryStatus.Completed);
    }

    [Fact]
    public async Task ConcurrentRemoval_StatusRetryReportsNotInLibrary_AndDoesNotResurrectEntry()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        context.Observer.BeforeSave = async (_, _) =>
        {
            context.Observer.BeforeSave = null;
            await context.RemoveAsync(observe: false);
        };
        var error = await Assert.ThrowsAsync<LibraryException>(() => context.ChangeAsync(LibraryStatus.Completed));
        Assert.Equal("not_in_library", error.Code);
        Assert.Null(await context.EntryAsync());
        Assert.Equal(2, (await context.EventsAsync()).Count);
    }

    [Fact]
    public async Task RemovalRacingStatusChange_RetriesAndRecordsLatestFromStatus()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        context.Observer.BeforeSave = async (_, _) =>
        {
            context.Observer.BeforeSave = null;
            await context.ChangeAsync(LibraryStatus.Beaten, observe: false);
        };
        await context.RemoveAsync();
        Assert.Null(await context.EntryAsync());
        var events = await context.EventsAsync();
        Assert.Equal(3, events.Count);
        Assert.Equal(LibraryEventType.Removed, events.Last().Type);
        Assert.Equal(LibraryStatus.Beaten, events.Last().FromStatus);
    }

    [Fact]
    public async Task LongLivedScope_FreshReadsObserveOtherWriters_AndKeepNoTrackedLibraryEntities()
    {
        using var context = new LibraryTestContext();
        await using var db = context.CreateDb();
        var service = context.Service(db);
        await service.AddAsync(context.UserId, context.GameId, LibraryStatus.Backlog);
        await context.ChangeAsync(LibraryStatus.Beaten);
        var current = await service.ChangeStatusAsync(context.UserId, context.GameId, LibraryStatus.Completed);
        Assert.Equal(LibraryStatus.Completed, current.Status);
        Assert.Equal(LibraryStatus.Beaten, (await context.EventsAsync()).Last().FromStatus);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
