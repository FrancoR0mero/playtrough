using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class ReviewConcurrencyTests
{
    [Fact]
    public async Task ConcurrentCreates_DifferentUsers_KeepExactCountAndSum()
    {
        using var context = new ReviewTestContext();
        var users = context.UserIds.Take(8).ToArray();
        var ratings = users.Select((_, index) => 1m + index * 0.5m).ToArray();
        SynchronizeFirstReads(context, users.Length);
        var results = await Task.WhenAll(users.Select((user, index) =>
            Task.Run(() => context.UpsertAsync(ratings[index], userId: user))));
        Assert.All(results, result => Assert.True(result.Created));
        var game = await context.GameAsync();
        Assert.Equal(users.Length, game.RatingCount);
        Assert.Equal(ratings.Sum(), game.RatingSum);
        await using var db = context.CreateDb();
        Assert.Equal(users.Length, await db.Reviews.CountAsync());
        Assert.Equal(ratings.Sum(), (await db.Reviews.ToListAsync()).Sum(review => review.Rating));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCreates_SameUser_OneReviewAndOneCreate_OtherRequestBecomesEdit(bool sameRating)
    {
        using var context = new ReviewTestContext();
        SynchronizeFirstReads(context, 2);
        var results = await Task.WhenAll(Task.Run(() => context.UpsertAsync(2m)),
            Task.Run(() => context.UpsertAsync(sameRating ? 2m : 4.5m)));
        Assert.Single(results, result => result.Created);
        Assert.Equal(results[0].Review.Id, results[1].Review.Id);
        var winner = (await context.MineAsync()).Review;
        var game = await context.GameAsync();
        Assert.Equal(1, game.RatingCount);
        Assert.Equal(winner.Rating, game.RatingSum);
        Assert.Equal(sameRating, winner.EditedAt is null);
        // Both reads missed; the unique constraint actually rejected the losing INSERT.
        Assert.Equal(2, context.Observer.Commands.Count(sql => sql.StartsWith("INSERT INTO \"Reviews\"")));
        await using var db = context.CreateDb();
        Assert.Single(await db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentEdits_RetryWithWinningRating_WithoutCounterDrift()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(1m);
        SynchronizeFirstReads(context, 2);
        var results = await Task.WhenAll(Task.Run(() => context.UpsertAsync(2.5m)),
            Task.Run(() => context.UpsertAsync(5m)));
        Assert.All(results, result => Assert.False(result.Created));
        var review = (await context.MineAsync()).Review;
        Assert.Equal(review.Rating, (await context.GameAsync()).RatingSum);
        Assert.Equal(1, (await context.GameAsync()).RatingCount);
    }

    [Fact]
    public async Task FirstEditConflict_RereadsWinner_AndAppliesDifferenceFromLatestRating()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(1m);
        var reads = 0;
        context.Observer.AfterReviewRead = async _ =>
        {
            if (++reads == 1) await context.UpsertAsync(4m, observe: false);
        };
        var result = await context.UpsertAsync(2.5m);
        Assert.Equal(2, reads);
        Assert.Equal(2.5m, result.Review.Rating);
        Assert.Equal(new RatingSummary(2.5m, 1), result.RatingSummary);
        Assert.Equal(2.5m, (await context.GameAsync()).RatingSum);
    }

    [Fact]
    public async Task Retry_WhenWinnerAlreadyMatches_IsIdempotent()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(1m);
        ReviewResult? winner = null;
        var reads = 0;
        context.Observer.AfterReviewRead = async _ =>
        {
            if (++reads == 1) winner = await context.UpsertAsync(3.5m, "igual", observe: false);
        };
        var result = await context.UpsertAsync(3.5m, "igual");
        Assert.Equal(2, reads);
        Assert.Equal(winner!.Review.ConcurrencyToken, result.Review.ConcurrencyToken);
        Assert.Equal(winner.Review.EditedAt, result.Review.EditedAt);
        Assert.Equal(new RatingSummary(3.5m, 1), result.RatingSummary);
    }

    [Fact]
    public async Task TwoEditConflicts_ReportConcurrentModification_WithoutThirdAttemptOrCounterLeak()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(1m);
        var reads = 0;
        context.Observer.AfterReviewRead = async _ =>
        {
            await context.UpsertAsync(++reads == 1 ? 2m : 3m, observe: false);
        };
        await using var db = context.CreateDb();
        var error = await Assert.ThrowsAsync<ReviewException>(() =>
            context.Service(db).UpsertAsync(context.UserIds[0], context.GameId, 5m, null));
        Assert.Equal("concurrent_modification", error.Code);
        Assert.Equal(2, reads);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await db.SaveChangesAsync());
        Assert.Equal(3m, (await context.GameAsync()).RatingSum);
        Assert.Equal(3m, (await context.MineAsync()).Review.Rating);
    }

    [Fact]
    public async Task DeleteRacingEdit_SubtractsWinningRating()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(1m);
        var reads = 0;
        context.Observer.AfterReviewRead = async _ =>
        {
            if (++reads == 1) await context.UpsertAsync(4.5m, observe: false);
        };
        Assert.Equal(new RatingSummary(null, 0), await context.DeleteAsync());
        Assert.Equal(2, reads);
        Assert.Equal(0m, (await context.GameAsync()).RatingSum);
    }

    [Fact]
    public async Task TwoDeleteConflicts_ReportConcurrentModification_AndKeepWinner()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(1m);
        var reads = 0;
        context.Observer.AfterReviewRead = async _ =>
        {
            await context.UpsertAsync(++reads == 1 ? 2m : 3m, observe: false);
        };
        var error = await Assert.ThrowsAsync<ReviewException>(() => context.DeleteAsync());
        Assert.Equal("concurrent_modification", error.Code);
        Assert.Equal(2, reads);
        Assert.Equal(3m, (await context.GameAsync()).RatingSum);
        Assert.Equal(3m, (await context.MineAsync()).Review.Rating);
    }

    [Fact]
    public async Task ConcurrentDeletes_OneSucceeds_OtherIsNotFound_AndCountersStayZero()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync();
        SynchronizeFirstReads(context, 2);
        async Task<string> Delete()
        {
            try { await context.DeleteAsync(); return "deleted"; }
            catch (ReviewException error) { return error.Code; }
        }
        var results = await Task.WhenAll(Task.Run(Delete), Task.Run(Delete));
        Assert.Contains("deleted", results);
        Assert.Contains("review_not_found", results);
        var game = await context.GameAsync();
        Assert.Equal(0, game.RatingCount);
        Assert.Equal(0m, game.RatingSum);
    }

    private static void SynchronizeFirstReads(ReviewTestContext context, int writers)
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        context.Observer.AfterReviewRead = async ct =>
        {
            if (Interlocked.Increment(ref arrivals) <= writers)
            {
                if (Volatile.Read(ref arrivals) == writers) barrier.TrySetResult();
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
        };
    }
}
