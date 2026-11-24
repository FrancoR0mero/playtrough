using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;

namespace Playtrough.Api.Events;

internal static class OutboxEvents
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static OutboxMessage Enqueue<T>(AppDbContext db, long aggregate, T change, DateTime now) where T : IDomainEvent
    {
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(), Type = typeof(T).Name, AggregateId = aggregate.ToString(CultureInfo.InvariantCulture),
            Payload = JsonSerializer.Serialize(change, Json), OccurredAt = now,
        };
        db.OutboxMessages.Add(row);
        return row;
    }

    internal static async Task SaveAsync<T>(AppDbContext db, long aggregate, T change, DateTime now, CancellationToken ct)
        where T : IDomainEvent
    {
        var row = Enqueue(db, aggregate, change, now);
        try { await db.SaveChangesAsync(ct); }
        finally { db.Entry(row).State = EntityState.Detached; }
    }
}
