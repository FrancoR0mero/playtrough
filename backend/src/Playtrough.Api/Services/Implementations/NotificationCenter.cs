using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Hubs;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class NotificationCenter(AppDbContext db, TimelineEntryAccess timeline, GroupAccess groups,
    IDataProtectionProvider protection, TimeProvider clock, IHubContext<NotificationHub> hub, ILogger<NotificationCenter> logger)
{
    internal const string TimelineEntry = "timeline-entry";
    internal const string Group = "group";
    private const string Page = "page";
    private const string Cutoff = "cutoff";
    private sealed record Position(DateTime CreatedAt, long Id, long HighId);

    internal async Task<NotificationPageDto?> ListAsync(int user, string? cursor, int limit, CancellationToken ct)
    {
        var owned = Owned(user);
        var query = owned;
        if (cursor is not null)
        {
            if (Decode(user, Page, cursor) is not { } position) return null;
            query = query.Where(row => row.CreatedAt < position.CreatedAt
                || row.CreatedAt == position.CreatedAt && row.Id < position.Id);
        }

        var unblocked = PrivacyService.UnblockedUsers(db, user);
        var entries = timeline.VisibleTo(user);
        var memberGroups = groups.MemberGroups(user);
        var firstPage = cursor is null;
        var rows = await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Take(limit + 1)
            .Select(row => new
            {
                Row = row,
                HighId = firstPage ? owned.Max(item => (long?)item.Id) : null,
                Accessible = unblocked.Any(actor => actor.Id == row.ActorId) &&
                    (row.ResourceType == TimelineEntry && entries.Any(entry => entry.Id == row.ResourceId)
                     || row.ResourceType == Group && memberGroups.Any(group => group.Id == row.ResourceId)),
            }).ToListAsync(ct);
        var items = rows.Take(limit).Select(row => ToItem(row.Row, row.Accessible)).ToArray();
        var cutoff = firstPage && rows.Count > 0
            ? Encode(user, Cutoff, new(rows[0].Row.CreatedAt, rows[0].Row.Id, rows[0].HighId!.Value)) : null;
        var next = rows.Count > limit ? Encode(user, Page, new(rows[limit - 1].Row.CreatedAt, rows[limit - 1].Row.Id, 0)) : null;
        return new(items, next, cutoff);
    }

    internal async Task<NotificationCountDto> CountAsync(int user, CancellationToken ct)
        => new(await Owned(user).CountAsync(row => row.ReadAt == null, ct));

    internal async Task<bool> ReadAsync(int user, long id, CancellationToken ct)
    {
        var owned = Owned(user).Where(row => row.Id == id);
        var changed = await MarkAsync(owned, ct);
        if (changed > 0) await InvalidateReadersAsync(user, ct);
        return changed > 0 || await owned.AnyAsync(ct);
    }

    internal async Task<NotificationCountDto?> ReadAllAsync(int user, string? cutoff, CancellationToken ct)
    {
        if (Decode(user, Cutoff, cutoff) is not { } position) return null;
        var visibleAtCut = Owned(user).Where(row => row.Id <= position.HighId && (row.CreatedAt < position.CreatedAt
            || row.CreatedAt == position.CreatedAt && row.Id <= position.Id));
        if (await MarkAsync(visibleAtCut, ct) > 0) await InvalidateReadersAsync(user, ct);
        return await CountAsync(user, ct);
    }

    private IQueryable<Notification> Owned(int user) => db.Notifications.AsNoTracking().Where(row => row.RecipientId == user);

    private Task<int> MarkAsync(IQueryable<Notification> query, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return query.Where(row => row.ReadAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ReadAt, now), ct);
    }

    private async Task InvalidateReadersAsync(int user, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await hub.Clients.User(user.ToString(CultureInfo.InvariantCulture)).SendAsync("NotificationsRead", timeout.Token);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Notification read invalidation failed ({ExceptionType}).", error.GetType().Name);
        }
    }

    private static NotificationItemDto ToItem(Notification row, bool accessible)
    {
        var key = row.ResourceId?.ToString(CultureInfo.InvariantCulture);
        var (message, destination) = (accessible, row.ResourceType) switch
        {
            (true, TimelineEntry) => ("Hay actividad en una publicación.", $"/entries/{key}"),
            (true, Group) => ("Hay actividad en un grupo.", $"/groups/{key}"),
            (false, not null) => ("Este aviso ya no tiene detalles disponibles.", null),
            _ => ("Tenés una notificación.", null),
        };
        return new(row.Id.ToString(CultureInfo.InvariantCulture), row.CreatedAt, row.ReadAt, message, destination);
    }

    private IDataProtector Protector(int user, string purpose)
        => protection.CreateProtector("Playtrough.Notifications.v1", purpose, user.ToString(CultureInfo.InvariantCulture));

    private string Encode(int user, string purpose, Position position)
        => Protector(user, purpose).Protect(JsonSerializer.Serialize(position));

    private Position? Decode(int user, string purpose, string? token)
    {
        if (token is null || token.Length is < 1 or > 1024) return null;
        try { return JsonSerializer.Deserialize<Position>(Protector(user, purpose).Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return null; }
    }
}
