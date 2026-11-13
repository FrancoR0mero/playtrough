using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class FriendService(AppDbContext db, ILookupNormalizer normalizer, TimeProvider time,
    IOptions<FriendsSettings> settings, IDomainEventPublisher events) : IFriendService
{
    public async Task<SentFriendRequestDto> SendRequestAsync(int senderId, string username, CancellationToken ct = default)
    {
        var receiver = await FindUserAsync(username, ct);
        if (receiver.Id == senderId) throw new FriendException("cannot_befriend_self");
        FriendRequest request;
        try
        {
            request = await WithPairAsync(senderId, receiver.Id, async () =>
            {
                // Recheck after obtaining the lock: the target may have renamed or become unconfirmed.
                var currentReceiver = await FindUserAsync(username, ct);
                if (currentReceiver.Id != receiver.Id) throw new FriendException("user_not_found");
                receiver = currentReceiver;
                await EnsureNoRelationshipAsync(senderId, receiver.Id, ct);
                var lastRejection = await db.FriendRequests.Where(r => r.SenderId == senderId &&
                        r.ReceiverId == receiver.Id && r.Status == FriendRequestStatus.Rejected)
                    .MaxAsync(r => r.RespondedAt, ct);
                if (lastRejection?.AddDays(settings.Value.RejectCooldownDays) is { } available && available > time.GetUtcNow().UtcDateTime)
                    throw new FriendException("cooldown_active", available);
                if (await db.FriendRequests.CountAsync(r => r.SenderId == senderId && r.Status == FriendRequestStatus.Pending, ct)
                    >= settings.Value.MaxPendingSent) throw new FriendException("too_many_pending_requests");
                var row = new FriendRequest
                {
                    SenderId = senderId, ReceiverId = receiver.Id, CreatedAt = time.GetUtcNow().UtcDateTime,
                    Status = FriendRequestStatus.Pending,
                };
                db.FriendRequests.Add(row);
                await db.SaveChangesAsync(ct);
                return row;
            }, ct);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // Recover a unique-key race only if the winning relationship actually exists.
            await EnsureNoRelationshipAsync(senderId, receiver.Id, ct);
            throw;
        }
        await events.PublishAsync(new FriendRequestSent(request.Id, senderId, receiver.Id, request.CreatedAt), CancellationToken.None);
        return new(request.Id, "pending", new(receiver.UserName!, AvatarUrls.FromFile(receiver.AvatarFileName)), request.CreatedAt);
    }

    public async Task<AcceptedFriendDto> AcceptAsync(int userId, int requestId, CancellationToken ct = default)
    {
        var initial = await OwnRequestAsync(userId, requestId, receiver: true, ct);
        (AcceptedFriendDto Result, FriendRequestAccepted? Event) outcome;
        try
        {
            outcome = await WithPairAsync(initial.SenderId, initial.ReceiverId, async () =>
            {
                var request = await OwnRequestAsync(userId, requestId, receiver: true, ct);
                var sender = await db.Users.AsNoTracking().SingleAsync(u => u.Id == request.SenderId, ct);
                if (request.Status == FriendRequestStatus.Accepted)
                    return (new AcceptedFriendDto(new(sender.UserName!, AvatarUrls.FromFile(sender.AvatarFileName)), request.RespondedAt!.Value), (FriendRequestAccepted?)null);
                if (request.Status != FriendRequestStatus.Pending) throw new FriendException("request_not_pending");
                var now = time.GetUtcNow().UtcDateTime;
                await db.FriendRequests.Where(r => r.Id == requestId).ExecuteUpdateAsync(update =>
                    update.SetProperty(r => r.Status, FriendRequestStatus.Accepted).SetProperty(r => r.RespondedAt, now), ct);
                var (low, high) = Pair(request.SenderId, userId);
                db.Friendships.Add(new Friendship { UserLowId = low, UserHighId = high, CreatedAt = now });
                await db.SaveChangesAsync(ct);
                return (new AcceptedFriendDto(new(sender.UserName!, AvatarUrls.FromFile(sender.AvatarFileName)), now),
                    (FriendRequestAccepted?)new FriendRequestAccepted(requestId, request.SenderId, userId, now));
            }, ct);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // Rollback has completed. A concurrent winner must have committed both rows.
            var current = await OwnRequestAsync(userId, requestId, receiver: true, ct);
            var (low, high) = Pair(current.SenderId, userId);
            if (current.Status != FriendRequestStatus.Accepted ||
                !await db.Friendships.AnyAsync(f => f.UserLowId == low && f.UserHighId == high, ct)) throw;
            var sender = await db.Users.AsNoTracking().SingleAsync(u => u.Id == current.SenderId, ct);
            return new(new(sender.UserName!, AvatarUrls.FromFile(sender.AvatarFileName)), current.RespondedAt!.Value);
        }
        if (outcome.Event is { } accepted) await events.PublishAsync(accepted, CancellationToken.None);
        return outcome.Result;
    }

    public Task RejectAsync(int userId, int requestId, CancellationToken ct = default)
        => FinishRequestAsync(userId, requestId, FriendRequestStatus.Rejected, receiver: true, ct);
    public Task CancelAsync(int userId, int requestId, CancellationToken ct = default)
        => FinishRequestAsync(userId, requestId, FriendRequestStatus.Canceled, receiver: false, ct);

    private async Task FinishRequestAsync(int userId, int requestId, FriendRequestStatus status, bool receiver, CancellationToken ct)
    {
        var initial = await OwnRequestAsync(userId, requestId, receiver, ct);
        await WithPairAsync(initial.SenderId, initial.ReceiverId, async () =>
        {
            var current = await OwnRequestAsync(userId, requestId, receiver, ct);
            if (current.Status == status) return true;
            if (current.Status != FriendRequestStatus.Pending) throw new FriendException("request_not_pending");
            var now = time.GetUtcNow().UtcDateTime;
            await db.FriendRequests.Where(r => r.Id == requestId).ExecuteUpdateAsync(update =>
                update.SetProperty(r => r.Status, status).SetProperty(r => r.RespondedAt, now), ct);
            return true;
        }, ct);
    }

    public async Task RemoveFriendAsync(int userId, string username, CancellationToken ct = default)
    {
        var other = await FindUserAsync(username, ct);
        await WithPairAsync(userId, other.Id, async () =>
        {
            var (low, high) = Pair(userId, other.Id);
            await db.Friendships.Where(f => f.UserLowId == low && f.UserHighId == high).ExecuteDeleteAsync(ct);
            return true;
        }, ct);
    }

    public async Task<FriendsPage<FriendDto>> ListAsync(int userId, string? q, FriendsPageRequest page, CancellationToken ct = default)
    {
        var skip = Skip(page);
        var query = from friendship in db.Friendships.AsNoTracking()
                    where friendship.UserLowId == userId || friendship.UserHighId == userId
                    join user in db.Users on (friendship.UserLowId == userId ? friendship.UserHighId : friendship.UserLowId) equals user.Id
                    where user.EmailConfirmed
                    select new { user.Id, user.UserName, user.NormalizedUserName, user.AvatarFileName, friendship.CreatedAt };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + LikePattern.Escape(normalizer.NormalizeName(q.Trim())) + "%";
            query = query.Where(row => EF.Functions.Like(row.NormalizedUserName!, pattern, LikePattern.EscapeCharacter));
        }
        var rows = await query.OrderBy(row => row.NormalizedUserName).ThenBy(row => row.Id)
            .Skip(skip).Take(page.PageSize + 1).ToListAsync(ct);
        return new(rows.Take(page.PageSize).Select(row => new FriendDto(new(row.UserName!, AvatarUrls.FromFile(row.AvatarFileName)), row.CreatedAt)).ToArray(),
            page.Page, page.PageSize, rows.Count > page.PageSize);
    }

    public async Task<FriendsPage<FriendRequestDto>> RequestsAsync(int userId, bool received, FriendsPageRequest page, CancellationToken ct = default)
    {
        var skip = Skip(page);
        var query = from request in db.FriendRequests.AsNoTracking()
                    where request.Status == FriendRequestStatus.Pending && (received ? request.ReceiverId == userId : request.SenderId == userId)
                    join user in db.Users on (received ? request.SenderId : request.ReceiverId) equals user.Id
                    where user.EmailConfirmed
                    orderby request.CreatedAt descending, request.Id descending
                    select new { request.Id, user.UserName, user.AvatarFileName, request.CreatedAt };
        var rows = await query.Skip(skip).Take(page.PageSize + 1).ToListAsync(ct);
        return new(rows.Take(page.PageSize).Select(row => new FriendRequestDto(row.Id, new(row.UserName!, AvatarUrls.FromFile(row.AvatarFileName)), row.CreatedAt)).ToArray(),
            page.Page, page.PageSize, rows.Count > page.PageSize);
    }

    public Task<FriendsSummaryDto> SummaryAsync(int userId, CancellationToken ct = default)
        => db.Users.Where(u => u.Id == userId).Select(u => new FriendsSummaryDto(
            db.Friendships.Count(f => f.UserLowId == userId || f.UserHighId == userId),
            db.FriendRequests.Count(r => r.ReceiverId == userId && r.Status == FriendRequestStatus.Pending),
            db.FriendRequests.Count(r => r.SenderId == userId && r.Status == FriendRequestStatus.Pending)))
            .SingleAsync(ct);

    public async Task<RelationshipDto> RelationshipAsync(int viewerId, int otherId, CancellationToken ct = default)
    {
        if (viewerId == otherId) return new("none");
        var (low, high) = Pair(viewerId, otherId);
        var key = PairKey(low, high);
        // UNION ALL performs one indexed lookup per table, in one database round trip.
        var row = await db.Friendships.Where(f => f.UserLowId == low && f.UserHighId == high)
            .Select(f => new { Status = "friends", RequestId = (int?)null, Priority = 0 })
            .Concat(db.FriendRequests.Where(r => r.PendingPairKey == key)
                .Select(r => new { Status = r.SenderId == viewerId ? "request_sent" : "request_received", RequestId = (int?)r.Id, Priority = 1 }))
            .OrderBy(r => r.Priority).FirstOrDefaultAsync(ct);
        return row is null ? new("none") : new(row.Status, row.RequestId);
    }

    private async Task EnsureNoRelationshipAsync(int sender, int receiver, CancellationToken ct)
    {
        var relationship = await RelationshipAsync(sender, receiver, ct);
        switch (relationship.Status)
        {
            case "friends": throw new FriendException("already_friends");
            case "request_sent": throw new FriendException("request_already_sent");
            case "request_received": throw new FriendException("request_already_received", requestId: relationship.RequestId);
        }
    }

    private async Task<ApplicationUser> FindUserAsync(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new FriendException("user_not_found");
        var normalized = normalizer.NormalizeName(username.Trim());
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedUserName == normalized && u.EmailConfirmed, ct)
            ?? throw new FriendException("user_not_found");
    }
    private async Task<FriendRequest> OwnRequestAsync(int userId, int requestId, bool receiver, CancellationToken ct)
        => await db.FriendRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId &&
            (receiver ? r.ReceiverId == userId : r.SenderId == userId), ct) ?? throw new FriendException("request_not_found");

    private Task<T> WithPairAsync<T>(int first, int second, Func<Task<T>> action, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var (low, high) = Pair(first, second);
                // No-op updates acquire database row locks (not in-process locks). Always
                // low then high, before any transactional read, avoiding stale snapshots.
                // Locking sender also serializes its pending quota across different pairs.
                await db.Users.Where(u => u.Id == low).ExecuteUpdateAsync(update =>
                    update.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount), ct);
                if (high != low)
                    await db.Users.Where(u => u.Id == high).ExecuteUpdateAsync(update =>
                        update.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount), ct);
                var result = await action();
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is FriendRequest or Friendship).ToArray())
                    entry.State = EntityState.Detached;
                throw;
            }
        });

    private static (int Low, int High) Pair(int a, int b) => a < b ? (a, b) : (b, a);
    private static long PairKey(int low, int high) => (long)low * 4294967296L + high;
    private static int Skip(FriendsPageRequest page)
    {
        if (page.Page < 1 || page.PageSize is < 1 or > 50 || ((long)page.Page - 1) * page.PageSize > int.MaxValue)
            throw new FriendException("invalid_query");
        return (page.Page - 1) * page.PageSize;
    }
}
