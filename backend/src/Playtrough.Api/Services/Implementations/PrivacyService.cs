using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class PrivacyService(AppDbContext db) : IPrivacyService
{
    public bool CanView(int viewerId, int ownerId, Visibility visibility, bool areFriends, bool blocked = false)
        => viewerId > 0 && (viewerId == ownerId || !blocked && (visibility == Visibility.Public ||
            visibility == Visibility.Friends && areFriends));

    public Task<bool> IsBlockedAsync(int firstId, int secondId, CancellationToken ct = default)
        => firstId == secondId ? Task.FromResult(false) : db.UserBlocks.AnyAsync(b =>
            b.BlockerId == firstId && b.BlockedId == secondId || b.BlockerId == secondId && b.BlockedId == firstId, ct);

    public async Task<bool> CanViewAsync(int viewerId, int ownerId, PrivacyScope scope, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (viewerId <= 0) return false;
        var low = Math.Min(viewerId, ownerId);
        var high = Math.Max(viewerId, ownerId);
        // Fresh no-tracking query, including one existence subquery for Friends.
        var row = await db.Users.AsNoTracking().Where(u => u.Id == ownerId).Select(u => new
        {
            Blocked = viewerId != ownerId && db.UserBlocks.Any(b =>
                b.BlockerId == viewerId && b.BlockedId == ownerId || b.BlockerId == ownerId && b.BlockedId == viewerId),
            Visibility = scope == PrivacyScope.Profile ? u.ProfileVisibility : u.HistoryVisibility,
            AreFriends = viewerId != ownerId &&
                (scope == PrivacyScope.Profile ? u.ProfileVisibility : u.HistoryVisibility) == Visibility.Friends &&
                db.Friendships.Any(f => f.UserLowId == low && f.UserHighId == high),
        }).SingleOrDefaultAsync(ct) ?? throw new ProfileException("user_not_found");
        return CanView(viewerId, ownerId, row.Visibility, row.AreFriends, row.Blocked);
    }

    public async Task<PrivacyDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.ProfileVisibility, u.HistoryVisibility }).SingleOrDefaultAsync(ct)
            ?? throw new ProfileException("user_not_found");
        return new(Name(row.ProfileVisibility), Name(row.HistoryVisibility));
    }

    public async Task<PrivacyDto> UpdateAsync(int userId, UpdatePrivacyRequest request, CancellationToken ct = default)
    {
        var profile = Parse(request.ProfileVisibility);
        var history = Parse(request.HistoryVisibility);
        var stamp = Guid.NewGuid().ToString();
        // Atomic update of just the two settings. Advance Identity's concurrency
        // stamp so a stale concurrent profile edit cannot overwrite this change.
        var changed = await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(update =>
            update.SetProperty(u => u.ProfileVisibility, profile).SetProperty(u => u.HistoryVisibility, history)
                .SetProperty(u => u.ConcurrencyStamp, stamp), ct);
        if (changed == 0) throw new ProfileException("user_not_found");
        return new(Name(profile), Name(history));
    }

    private static Visibility Parse(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() switch
    {
        "public" => Visibility.Public,
        "friends" => Visibility.Friends,
        "private" => Visibility.Private,
        _ => throw new ProfileException("invalid_visibility"),
    } : throw new ProfileException("invalid_visibility");

    private static string Name(Visibility value) => value switch
    {
        Visibility.Public => "public",
        Visibility.Friends => "friends",
        Visibility.Private => "private",
        _ => throw new InvalidOperationException("Invalid persisted visibility."),
    };
}
