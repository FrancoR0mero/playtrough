using System.Text;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class ReviewService(AppDbContext db, TimeProvider time) : IReviewService
{
    public Task<ReviewResult> UpsertAsync(int userId, int gameId, decimal rating, string? text, CancellationToken ct = default)
    {
        if (rating < 1m || rating > 5m || rating % 0.5m != 0m) throw new ReviewException("invalid_rating");
        var cleanText = Sanitize(text);
        // Retry the whole unit of work, never an individual command inside a user transaction.
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (!await db.Games.AnyAsync(game => game.Id == gameId, ct)) throw new ReviewException("game_not_found");
            var conflicts = 0;
            var recoveredInsert = false;
            while (true)
            {
                // Read before opening the write transaction. The token protects this snapshot;
                // retries reread it without keeping an old MySQL/SQLite transaction snapshot.
                var review = await FindAsync(userId, gameId, ct);
                if (review is not null && review.Rating == rating && review.Text == cleanText)
                    return new ReviewResult(review, await GetSummaryAsync(gameId, ct));

                var created = review is null;
                var previousRating = review?.Rating ?? 0m;
                review ??= new Review
                {
                    UserId = userId, GameId = gameId, CreatedAt = time.GetUtcNow().UtcDateTime,
                };
                var previousToken = review.ConcurrencyToken;
                review.Rating = rating;
                review.Text = cleanText;
                review.ConcurrencyToken = Guid.NewGuid();
                if (!created) review.EditedAt = time.GetUtcNow().UtcDateTime;
                try
                {
                    var summary = await SaveAsync(review, previousToken, previousRating, created, delete: false, ct);
                    return new ReviewResult(review, summary, created);
                }
                catch (DbUpdateConcurrencyException) when (!ct.IsCancellationRequested)
                {
                    if (++conflicts == 2) throw new ReviewException("concurrent_modification");
                }
                catch (DbUpdateException) when (created && !ct.IsCancellationRequested)
                {
                    // Match the existing repository convention: only recover an insert failure
                    // when the unique identity actually exists after rollback. Otherwise rethrow.
                    if (await FindAsync(userId, gameId, ct) is null) throw;
                    if (recoveredInsert) throw new ReviewException("concurrent_modification");
                    recoveredInsert = true;
                }
            }
        });
    }

    public Task<RatingSummary> DeleteAsync(int userId, int gameId, CancellationToken ct = default)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var review = await FindAsync(userId, gameId, ct) ?? throw new ReviewException("review_not_found");
                try
                {
                    return await SaveAsync(review, review.ConcurrencyToken, review.Rating, created: false, delete: true, ct);
                }
                catch (DbUpdateConcurrencyException) when (!ct.IsCancellationRequested)
                {
                    if (attempt == 1) throw new ReviewException("concurrent_modification");
                }
            }
            throw new ReviewException("concurrent_modification");
        });

    public async Task<ReviewResult> GetMineAsync(int userId, int gameId, CancellationToken ct = default)
    {
        // Review and aggregate come from one SQL statement, without loading user data.
        var row = await (from review in db.Reviews.AsNoTracking()
                         join game in db.Games.AsNoTracking() on review.GameId equals game.Id
                         where review.UserId == userId && review.GameId == gameId
                         select new { Review = review, game.RatingCount, game.RatingSum }).SingleOrDefaultAsync(ct)
                  ?? throw new ReviewException("review_not_found");
        return new(row.Review, RatingSummary.FromTotals(row.RatingCount, row.RatingSum));
    }

    public async Task<RatingSummary> GetSummaryAsync(int gameId, CancellationToken ct = default)
    {
        var totals = await db.Games.AsNoTracking().Where(game => game.Id == gameId)
            .Select(game => new { game.RatingCount, game.RatingSum }).SingleOrDefaultAsync(ct)
            ?? throw new ReviewException("game_not_found");
        return RatingSummary.FromTotals(totals.RatingCount, totals.RatingSum);
    }

    private Task<Review?> FindAsync(int userId, int gameId, CancellationToken ct)
        => db.Reviews.AsNoTracking().SingleOrDefaultAsync(review => review.UserId == userId && review.GameId == gameId, ct);

    public Task<ReviewListResponse> ListAsync(int userId, int gameId, ReviewListRequest request, CancellationToken ct = default)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Keep the aggregate and page in the same consistent transaction, using
            // the isolation level already used by the library on both MySQL and SQLite.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var summary = await GetSummaryAsync(gameId, ct);
            var query = from review in db.Reviews.AsNoTracking()
                        join author in db.Users.AsNoTracking() on review.UserId equals author.Id
                        where review.GameId == gameId
                        select new
                        {
                            review.Id, review.Rating, review.Text, review.CreatedAt, review.EditedAt,
                            AuthorId = author.Id, Username = author.UserName,
                        };
            // Half-star units are exact integers. The integral ordering key works
            // on both providers without converting ratings to floating point.
            var ordered = request.Sort switch
            {
                "top" => query.OrderByDescending(review => (int)(review.Rating * 2m))
                    .ThenByDescending(review => review.CreatedAt).ThenByDescending(review => review.Id),
                "low" => query.OrderBy(review => (int)(review.Rating * 2m))
                    .ThenByDescending(review => review.CreatedAt).ThenByDescending(review => review.Id),
                _ => query.OrderByDescending(review => review.CreatedAt).ThenByDescending(review => review.Id),
            };
            var offset = ((long)request.Page - 1) * request.PageSize;
            var rows = offset > int.MaxValue ? [] : await ordered.Skip((int)offset).Take(request.PageSize + 1)
                .Select(review => new ReviewListItemDto(review.Id, review.Rating, review.Text, review.CreatedAt, review.EditedAt,
                    new ReviewAuthorDto(review.AuthorId, review.Username!), review.AuthorId == userId)).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            return new ReviewListResponse(rows.Take(request.PageSize).ToArray(), summary,
                request.Page, request.PageSize, rows.Count > request.PageSize);
        });

    private async Task<RatingSummary> SaveAsync(Review review, Guid previousToken, decimal previousRating,
        bool created, bool delete, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (created) db.Reviews.Add(review);
            else if (delete) db.Reviews.Remove(review);
            else
            {
                var entry = db.Reviews.Attach(review);
                entry.Property(value => value.Rating).IsModified = true;
                entry.Property(value => value.Text).IsModified = true;
                entry.Property(value => value.EditedAt).IsModified = true;
                entry.Property(value => value.ConcurrencyToken).IsModified = true;
                entry.Property(value => value.ConcurrencyToken).OriginalValue = previousToken;
            }
            var games = db.Games.Where(game => game.Id == review.GameId);
            // Lock/update the parent before writing the child. InnoDB FK checks take
            // shared parent locks; inserting first would allow concurrent creators to
            // deadlock while upgrading those locks to update the same game's counters.
            int affected;
            if (created)
                affected = await games.ExecuteUpdateAsync(update => update
                    .SetProperty(game => game.RatingCount, game => game.RatingCount + 1)
                    .SetProperty(game => game.RatingSum, game => game.RatingSum + review.Rating), ct);
            else if (delete)
                affected = await games.Where(game => game.RatingCount > 0 && game.RatingSum >= previousRating)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(game => game.RatingCount, game => game.RatingCount - 1)
                        .SetProperty(game => game.RatingSum, game => game.RatingSum - previousRating), ct);
            else
            {
                var delta = review.Rating - previousRating;
                affected = delta == 0m ? 1 : await games.Where(game => game.RatingCount > 0 && game.RatingSum + delta >= 0m)
                    .ExecuteUpdateAsync(update => update.SetProperty(game => game.RatingSum, game => game.RatingSum + delta), ct);
            }
            // Even if the guard rejected a stale decrement, check the review token
            // before reporting corruption: a concurrent edit/delete must be retried.
            await db.SaveChangesAsync(ct);
            if (affected != 1) throw new InvalidOperationException("Review counters are inconsistent.");
            var summary = await GetSummaryAsync(review.GameId, ct);
            await transaction.CommitAsync(ct);
            return summary;
        }
        finally
        {
            // Failed writes, execution-strategy retries and later saves cannot reuse stale state.
            db.Entry(review).State = EntityState.Detached;
        }
    }

    private static string? Sanitize(string? text)
    {
        var clean = PlainText.Sanitize(text);
        if (clean is null) return null;
        // Count Unicode scalar values: an emoji is one character, matching utf8mb4 VARCHAR.
        if (clean.EnumerateRunes().Count() > 2000) throw new ReviewException("invalid_text");
        return clean.Length == 0 ? null : clean;
    }
}
