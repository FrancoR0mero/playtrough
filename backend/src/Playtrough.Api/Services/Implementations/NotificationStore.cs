using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

internal sealed record NotificationDraft(int RecipientId, int? ActorId, string Type, string? ResourceType, long? ResourceId);

internal sealed class NotificationStore(AppDbContext db, TimeProvider clock)
{
    // A worker calls this with a committed outbox event before attempting real-time delivery.
    internal async Task<Notification> PersistAsync(Guid eventId, NotificationDraft draft, CancellationToken ct)
    {
        var matching = db.Notifications.AsNoTracking().Where(row => row.RecipientId == draft.RecipientId
            && row.EventId == eventId && row.Type == draft.Type);
        var existing = await matching.SingleOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var now = clock.GetUtcNow().UtcDateTime;
        var notification = new Notification
        {
            EventId = eventId,
            RecipientId = draft.RecipientId,
            ActorId = draft.ActorId,
            Type = draft.Type,
            ResourceType = draft.ResourceType,
            ResourceId = draft.ResourceId,
            CreatedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc),
            PayloadVersion = 1,
        };
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return notification;
        }
        catch (DbUpdateException error) when (error.Entries.Count == 1 && error.Entries[0].Entity == notification)
        {
            // A retry with an expired lease may race with the original worker.
            var winner = await matching.SingleOrDefaultAsync(ct);
            if (winner is null) throw;
            return winner;
        }
        finally
        {
            db.Entry(notification).State = EntityState.Detached;
        }
    }
}
