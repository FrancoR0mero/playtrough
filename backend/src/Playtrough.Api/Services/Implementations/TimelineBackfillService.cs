using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed record TimelineBackfillResult(long CreatedFromLibrary, long CreatedFromReviews);

public sealed class TimelineBackfillService(AppDbContext db, TimelineWriter writer, ILogger<TimelineBackfillService> logger)
{
    internal const int BatchSize = 500;

    public async Task<TimelineBackfillResult> RunAsync(CancellationToken ct = default)
    {
        long library = 0, reviews = 0;
        int count;
        do { count = await BatchAsync(false, ct); library += count; } while (count > 0);
        do { count = await BatchAsync(true, ct); reviews += count; } while (count > 0);
        logger.LogInformation("Timeline backfill completed. Library: {CreatedFromLibrary}. Reviews: {CreatedFromReviews}.", library, reviews);
        return new(library, reviews);
    }

    private Task<int> BatchAsync(bool reviews, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A competing backfill can win a unique source index. Roll back the
            // whole batch, verify that race, and select only the remaining sources.
            for (var attempt = 0; ; attempt++)
            {
                var entries = new List<TimelineEntry>();
                try
                {
                    if (reviews)
                    {
                        var sources = await db.Reviews.AsNoTracking()
                            .Where(source => !db.TimelineEntries.Any(entry => entry.ReviewId == source.Id))
                            .OrderBy(source => source.Id).Take(BatchSize).ToListAsync(ct);
                        foreach (var source in sources) entries.Add(writer.AddReview(source));
                    }
                    else
                    {
                        var sources = await db.LibraryEvents.AsNoTracking()
                            .Where(source => (source.Type == LibraryEventType.Added || source.Type == LibraryEventType.StatusChanged)
                                && !db.TimelineEntries.Any(entry => entry.LibraryEventId == source.Id))
                            .OrderBy(source => source.Id).Take(BatchSize).ToListAsync(ct);
                        foreach (var source in sources) entries.Add(writer.AddLibraryEvent(source)!);
                    }
                    // SaveChanges wraps all inserts of this batch in one transaction.
                    // Keep the preceding source reads outside it to avoid locking
                    // missing index ranges against another backfill or live writes.
                    if (entries.Count > 0) await db.SaveChangesAsync(ct);
                    return entries.Count;
                }
                catch (DbUpdateException) when (!ct.IsCancellationRequested && attempt < 2)
                {
                    foreach (var entry in entries) db.Entry(entry).State = EntityState.Detached;
                    var eventIds = entries.Where(e => e.LibraryEventId != null).Select(e => e.LibraryEventId!.Value).ToArray();
                    var reviewIds = entries.Where(e => e.ReviewId != null).Select(e => e.ReviewId!.Value).ToArray();
                    if (!await db.TimelineEntries.AsNoTracking().AnyAsync(e =>
                        eventIds.Contains(e.LibraryEventId ?? 0) || reviewIds.Contains(e.ReviewId ?? 0), ct)) throw;
                }
                finally
                {
                    foreach (var entry in entries) db.Entry(entry).State = EntityState.Detached;
                }
            }
        });
}
