using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class TimelineService(AppDbContext db, ILookupNormalizer normalizer, IPrivacyService privacy,
    TimelineQuery query, TimelineCursorCodec cursors) : ITimelineService
{
    public Task<TimelineEntryDto> SetVisibilityAsync(int ownerId, long id, UpdateTimelineVisibilityRequest request, CancellationToken ct = default)
    {
        if (request.Hidden.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new TimelineException("invalid_body");
        var hidden = request.Hidden.GetBoolean();
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Ownership is part of the UPDATE predicate, never an author supplied in the body.
            await db.TimelineEntries.Where(e => e.Id == id && e.UserId == ownerId)
                .ExecuteUpdateAsync(update => update.SetProperty(e => e.IsHidden, hidden), ct);
            // Read even when no value changed: affected-row semantics vary by MySQL configuration.
            var entry = await query.EntryAsync(ownerId, id, ct) ?? throw new TimelineException("entry_not_found");
            await transaction.CommitAsync(ct);
            return entry;
        });
    }

    public async Task<TimelinePageDto> ListAsync(int viewerId, string? username, TimelineListRequest request, CancellationToken ct = default)
    {
        var filters = query.Filters(request.Year, request.Month, request.Type);
        var limit = query.Limit(request.Limit);
        var ownerId = await OwnerAsync(viewerId, username, ct);
        var own = viewerId == ownerId;
        var context = FormattableString.Invariant($"timeline:{ownerId}:{viewerId}:{own}:{filters.Fingerprint}");
        var position = request.Cursor is null ? null : cursors.Decode(request.Cursor, context);
        var rows = await query.ListAsync(ownerId, own, filters, position, limit, viewerId, ct);
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).ToArray();
        var last = items.LastOrDefault();
        return new(items, hasMore && last is not null
            ? cursors.Encode(new(last.OccurredOn, last.RecordedAt, last.Id), context) : null, hasMore);
    }

    public async Task<TimelinePeriodsDto> PeriodsAsync(int viewerId, string? username, string? type, CancellationToken ct = default)
    {
        var filters = query.Filters(null, null, type);
        var ownerId = await OwnerAsync(viewerId, username, ct);
        return await query.PeriodsAsync(ownerId, ownerId == viewerId, filters, ct);
    }

    private async Task<int> OwnerAsync(int viewerId, string? username, CancellationToken ct)
    {
        if (username is null) return viewerId;
        var normalized = normalizer.NormalizeName(username);
        var ownerId = await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalized &&
            (u.EmailConfirmed || u.Id == viewerId)).Select(u => (int?)u.Id).SingleOrDefaultAsync(ct)
            ?? throw new TimelineException("user_not_found");
        if (ownerId == viewerId) return ownerId;
        if (await privacy.IsBlockedAsync(viewerId, ownerId, ct)) throw new TimelineException("user_not_found");
        if (!await privacy.CanViewAsync(viewerId, ownerId, PrivacyScope.History, ct))
            throw new TimelineException("history_restricted", "private");
        return ownerId;
    }
}
