using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class TimelineEntryAccess(AppDbContext db)
{
    internal IQueryable<TimelineEntry> VisibleTo(int viewerId)
        => from entry in db.TimelineEntries.AsNoTracking()
           join owner in PrivacyService.VisibleUsers(db, viewerId) on entry.UserId equals owner.Id
           where owner.Id == viewerId || owner.EmailConfirmed && !entry.IsHidden
           select entry;
}
