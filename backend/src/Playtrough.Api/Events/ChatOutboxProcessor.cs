using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;

namespace Playtrough.Api.Events;

internal interface IChatOutboxHandler
{
    string EventType { get; }
    Task HandleAsync(OutboxMessage message, CancellationToken ct);
}

internal sealed class ChatOutboxProcessor(
    AppDbContext db, IEnumerable<IChatOutboxHandler> handlers, TimeProvider clock, ILogger<ChatOutboxProcessor> logger)
{
    internal const int MaxAttempts = 8;
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);

    public async Task ProcessBatchAsync(CancellationToken ct)
    {
        var registered = handlers.ToDictionary(handler => handler.EventType, StringComparer.Ordinal);
        if (registered.Count == 0) return;
        var types = registered.Keys.ToArray();
        var candidates = await Due(types, UtcNow()).OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id).Select(message => new { message.Id, message.Attempts }).Take(20).ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var now = UtcNow();
            var pending = Due(types, now).Where(message => message.Id == candidate.Id && message.Attempts == candidate.Attempts);
            if (candidate.Attempts >= MaxAttempts)
            {
                // An interrupted final attempt can leave an expired lease behind.
                await pending.ExecuteUpdateAsync(update => update
                    .SetProperty(message => message.FailedAt, now)
                    .SetProperty(message => message.NextAttemptAt, (DateTime?)null)
                    .SetProperty(message => message.LeaseId, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAt, (DateTime?)null), ct);
                continue;
            }

            var leaseId = Guid.NewGuid();
            var leaseUntil = now + LeaseDuration;
            if (await pending.ExecuteUpdateAsync(update => update
                .SetProperty(message => message.Attempts, message => message.Attempts + 1)
                .SetProperty(message => message.LeaseId, leaseId)
                .SetProperty(message => message.LeaseExpiresAt, leaseUntil), ct) != 1) continue;

            var claimed = db.OutboxMessages.AsNoTracking().Where(message => message.Id == candidate.Id && message.LeaseId == leaseId);
            var message = await claimed.SingleOrDefaultAsync(ct);
            if (message is null) continue;
            var delivered = await DispatchAsync(registered[message.Type], message, ct);
            now = UtcNow();
            DateTime? processedAt = delivered ? now : null;
            DateTime? failedAt = !delivered && message.Attempts >= MaxAttempts ? now : null;
            DateTime? nextAttemptAt = !delivered && failedAt is null
                ? now.AddSeconds(Math.Min(60, 1 << (message.Attempts - 1))) : null;

            // A late handler may finish after another worker took over its lease.
            await claimed.ExecuteUpdateAsync(update => update
                .SetProperty(item => item.ProcessedAt, processedAt)
                .SetProperty(item => item.FailedAt, failedAt)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseExpiresAt, (DateTime?)null), ct);
        }
    }

    private IQueryable<OutboxMessage> Due(string[] types, DateTime now)
        => db.OutboxMessages.AsNoTracking().Where(message => types.Contains(message.Type)
            && message.ProcessedAt == null && message.FailedAt == null
            && (message.NextAttemptAt == null || message.NextAttemptAt <= now)
            && (message.LeaseExpiresAt == null || message.LeaseExpiresAt <= now));

    private async Task<bool> DispatchAsync(IChatOutboxHandler handler, OutboxMessage message, CancellationToken ct)
    {
        try
        {
            await handler.HandleAsync(message, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning("Chat event {EventId} failed on attempt {Attempt} ({ExceptionType}).",
                message.Id, message.Attempts, error.GetType().Name);
            return false;
        }
    }

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;
}
