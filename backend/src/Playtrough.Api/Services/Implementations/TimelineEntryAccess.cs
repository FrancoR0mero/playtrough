using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class TimelineEntryAccess(AppDbContext db)
{
    internal Task LockAsync(long entryId, CancellationToken ct)
        // Lock the parent before any snapshot read so concurrent writes see the latest state.
        => db.TimelineEntries.Where(entry => entry.Id == entryId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(entry => entry.CommentCount, entry => entry.CommentCount), ct);

    internal Task<int?> OwnerAsync(long entryId, int viewerId, CancellationToken ct)
        => VisibleTo(viewerId).Where(entry => entry.Id == entryId)
            .Select(entry => (int?)entry.UserId).SingleOrDefaultAsync(ct);

    internal IQueryable<TimelineEntry> VisibleTo(int viewerId)
        => from entry in db.TimelineEntries.AsNoTracking()
           join owner in PrivacyService.VisibleUsers(db, viewerId) on entry.UserId equals owner.Id
           where owner.Id == viewerId || owner.EmailConfirmed && !entry.IsHidden
           select entry;
}
