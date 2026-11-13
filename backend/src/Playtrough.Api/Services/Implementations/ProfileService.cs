using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class ProfileService(AppDbContext db, UserManager<ApplicationUser> users,
    IOptions<ProfileSettings> settings, TimeProvider time, IFriendService friends, IPrivacyService privacy, ILibraryService library) : IProfileService
{
    public async Task<MyProfileDto> GetMineAsync(int userId, CancellationToken ct = default)
        => await MineAsync(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new ProfileException("user_not_found"), ct);

    public async Task<ProfileDto> GetAsync(int viewerId, string username, CancellationToken ct = default)
    {
        var user = await FindVisibleAccountAsync(viewerId, username, ct);
        var relationship = await friends.RelationshipAsync(viewerId, user.Id, ct);
        if (!privacy.CanView(viewerId, user.Id, user.ProfileVisibility, relationship.Status == "friends"))
            return new(user.UserName!, AvatarUrls.FromFile(user.AvatarFileName), null, null, null, relationship, true,
                user.ProfileVisibility == Visibility.Friends ? "friends_only" : "private");
        var summary = await friends.SummaryAsync(user.Id, ct);
        return new(user.UserName!, AvatarUrls.FromFile(user.AvatarFileName), user.Bio, user.CreatedAt, summary.FriendsCount, relationship, false, null);
    }

    public async Task<FriendsPage<FriendDto>> FriendsAsync(int viewerId, string username, FriendsPageRequest request, CancellationToken ct = default)
    {
        var user = await FindVisibleAccountAsync(viewerId, username, ct);
        if (!await privacy.CanViewAsync(viewerId, user.Id, PrivacyScope.Profile, ct))
            throw new ProfileException("profile_restricted");
        return await friends.ListAsync(user.Id, null, request, ct);
    }

    public async Task<LibraryListResponseDto> LibraryAsync(int viewerId, string username, LibraryListRequest request, CancellationToken ct = default)
    {
        var user = await FindVisibleAccountAsync(viewerId, username, ct);
        if (!await privacy.CanViewAsync(viewerId, user.Id, PrivacyScope.History, ct))
            throw new ProfileException("history_restricted");
        return await library.ListAsync(user.Id, request, ct);
    }

    private async Task<ApplicationUser> FindVisibleAccountAsync(int viewerId, string username, CancellationToken ct)
    {
        var normalized = users.NormalizeName(username);
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == normalized &&
            (u.EmailConfirmed || u.Id == viewerId), ct) ?? throw new ProfileException("user_not_found");
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
        return new(user.Id, user.UserName!, user.Email, AvatarUrls.FromFile(user.AvatarFileName), user.Bio, user.CreatedAt,
            summary.FriendsCount, summary.PendingReceivedCount, AvailableAt(user));
    }
}
