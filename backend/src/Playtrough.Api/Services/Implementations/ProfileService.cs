using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class ProfileService(AppDbContext db, UserManager<ApplicationUser> users,
    IOptions<ProfileSettings> settings, TimeProvider time, IFriendService friends) : IProfileService
{
    public async Task<MyProfileDto> GetMineAsync(int userId, CancellationToken ct = default)
        => await MineAsync(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new ProfileException("user_not_found"), ct);

    public async Task<ProfileDto> GetAsync(int viewerId, string username, CancellationToken ct = default)
    {
        var normalized = users.NormalizeName(username);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == normalized &&
            (u.EmailConfirmed || u.Id == viewerId), ct) ?? throw new ProfileException("user_not_found");
        var summary = await friends.SummaryAsync(user.Id, ct);
        var relationship = await friends.RelationshipAsync(viewerId, user.Id, ct);
        return new(user.UserName!, null, user.Bio, user.CreatedAt, summary.FriendsCount, relationship, false, null);
    }

    public async Task<MyProfileDto> UpdateAsync(int userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new ProfileException("user_not_found");
        var username = user.UserName!;
        if (request.Username.ValueKind != JsonValueKind.Undefined)
        {
            if (request.Username.ValueKind != JsonValueKind.String)
                throw new ProfileException("invalid_username");
            username = request.Username.GetString()!;
            if (!string.Equals(username, user.UserName, StringComparison.OrdinalIgnoreCase) &&
                UsernameRules.IsReserved(username, settings.Value)) throw new ProfileException("username_reserved");
            if (!UsernameRules.IsValid(username) && !string.Equals(username, user.UserName, StringComparison.OrdinalIgnoreCase))
                throw new ProfileException("invalid_username");
        }
        var changed = !string.Equals(username, user.UserName, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            if (AvailableAt(user) is { } available) throw new ProfileException("username_change_too_soon", available);
            var normalized = users.NormalizeName(username);
            if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized && u.Id != userId, ct))
                throw new ProfileException("username_taken");
        }
        var bio = user.Bio;
        if (request.Bio.ValueKind != JsonValueKind.Undefined)
        {
            if (request.Bio.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new ProfileException("invalid_bio");
            bio = PlainText.Sanitize(request.Bio.GetString());
            if (bio?.EnumerateRunes().Count() > 300) throw new ProfileException("invalid_bio");
        }
        if (!changed && bio == user.Bio) return await MineAsync(user, ct);
        user.Bio = bio;
        if (changed) user.UsernameChangedAt = time.GetUtcNow().UtcDateTime;
        try
        {
            // Identity saves name, normalization, bio and cooldown together and checks its concurrency stamp.
            var result = changed ? await users.SetUserNameAsync(user, username) : await users.UpdateAsync(user);
            if (!result.Succeeded)
            {
                db.Entry(user).State = EntityState.Detached;
                if (result.Errors.Any(e => e.Code == "DuplicateUserName")) throw new ProfileException("username_taken");
                if (result.Errors.Any(e => e.Code == "ConcurrencyFailure"))
                {
                    var current = await GetMineAsync(userId, ct);
                    if (changed && current.UsernameChangeAvailableAt is { } available)
                        throw new ProfileException("username_change_too_soon", available);
                    throw new ProfileException("concurrent_modification");
                }
                throw new ProfileException("invalid_username");
            }
        }
        catch (DbUpdateException error) when (error.InnerException is MySqlException { Number: 1062 })
        {
            db.Entry(user).State = EntityState.Detached;
            throw new ProfileException("username_taken");
        }
        return await MineAsync(user, ct);
    }

    private DateTime? AvailableAt(ApplicationUser user)
    {
        var available = user.UsernameChangedAt?.AddDays(settings.Value.UsernameChangeCooldownDays);
        return available > time.GetUtcNow().UtcDateTime ? available : null;
    }
    private async Task<MyProfileDto> MineAsync(ApplicationUser user, CancellationToken ct)
    {
        var summary = await friends.SummaryAsync(user.Id, ct);
        return new(user.Id, user.UserName!, user.Email, null, user.Bio, user.CreatedAt,
            summary.FriendsCount, summary.PendingReceivedCount, AvailableAt(user));
    }
}
