using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Events;
using Playtrough.Api.Hubs;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class GroupMembershipService(AppDbContext db, GroupAccess access, IPrivacyService privacy, TimeProvider clock,
    ChatConnections chat)
{
    public Task<GroupMembershipDto> JoinAsync(int groupId, int actor, CancellationToken ct)
        => WriteAsync(groupId, async group =>
        {
            if (group.OwnerId == actor) return new GroupMembershipDto("owner");
            var member = await FindMemberAsync(groupId, actor, ct);
            if (member is { RemovedAt: null }) return new GroupMembershipDto("member");
            var pending = await PendingAsync(groupId, actor, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            if (group.Privacy == GroupPrivacy.Public)
            {
                if (pending is not null)
                {
                    pending.Status = GroupJoinRequestStatus.Cancelled;
                    pending.ResolvedAt = now;
                }
                ActivateMember(groupId, actor, member, now);
                return new GroupMembershipDto("member");
            }
            if (await privacy.IsBlockedAsync(actor, group.OwnerId, ct)) throw MissingGroup();
            if (pending is null)
            {
                var request = new GroupJoinRequest { GroupId = groupId, UserId = actor, CreatedAt = now };
                db.GroupJoinRequests.Add(request);
                await db.SaveChangesAsync(ct);
                Record(groupId, new GroupJoinRequested(groupId, request.Id, group.OwnerId, actor), now);
            }
            return new GroupMembershipDto("pending");
        }, ct);

    public Task LeaveAsync(int groupId, int actor, CancellationToken ct)
        => WriteAsync(groupId, async group =>
        {
            if (group.OwnerId == actor) throw OwnerMembership();
            var member = await FindMemberAsync(groupId, actor, ct);
            if (member is { RemovedAt: null }) db.GroupMembers.Remove(member);
            var pending = await PendingAsync(groupId, actor, ct);
            if (pending is not null)
            {
                pending.Status = GroupJoinRequestStatus.Cancelled;
                pending.ResolvedAt = clock.GetUtcNow().UtcDateTime;
            }
            return true;
        }, ct, revokedUser: actor);

    public Task ResolveAsync(int groupId, int requestId, int actor, bool approve, CancellationToken ct)
        => WriteAsync(groupId, async group =>
        {
            RequireOwner(group, actor);
            var request = await db.GroupJoinRequests.SingleOrDefaultAsync(item => item.GroupId == groupId && item.Id == requestId, ct)
                ?? throw new GroupMembershipException(404, "group_request_not_found", "La solicitud no está disponible.");
            var resolution = approve ? GroupJoinRequestStatus.Approved : GroupJoinRequestStatus.Rejected;
            if (request.Status == resolution) return true;
            if (request.Status != GroupJoinRequestStatus.Pending)
                throw new GroupMembershipException(409, "group_request_resolved", "La solicitud ya fue resuelta o cancelada.");
            if (await privacy.IsBlockedAsync(actor, request.UserId, ct))
                throw new GroupMembershipException(404, "group_request_not_found", "La solicitud no está disponible.");

            var now = clock.GetUtcNow().UtcDateTime;
            request.Status = resolution;
            request.ResolvedAt = now;
            if (approve)
                ActivateMember(groupId, request.UserId, await FindMemberAsync(groupId, request.UserId, ct), now);
            Record(groupId, new GroupJoinResolved(groupId, request.Id, actor, request.UserId, approve), now);
            return true;
        }, ct);

    public Task RemoveAsync(int groupId, int userId, int actor, CancellationToken ct)
        => WriteAsync(groupId, async group =>
        {
            RequireOwner(group, actor);
            if (userId == group.OwnerId) throw OwnerMembership();
            var member = await FindMemberAsync(groupId, userId, ct)
                ?? throw new GroupMembershipException(404, "group_member_not_found", "El miembro no está disponible.");
            if (member.RemovedAt is not null) return true;
            var now = clock.GetUtcNow().UtcDateTime;
            member.RemovedAt = now;
            Record(groupId, new GroupMemberRemoved(groupId, actor, userId), now);
            return true;
        }, ct, revokedUser: userId);

    public async Task<GroupPeoplePageDto<GroupMemberDto>> ListMembersAsync(int groupId, int actor, int cursor, int limit, CancellationToken ct)
    {
        var eligible = from member in access.ActiveMembers()
                       join user in PrivacyService.UnblockedUsers(db, actor) on member.UserId equals user.Id
                       where member.GroupId == groupId && member.UserId > cursor && user.EmailConfirmed
                       orderby member.UserId
                       select new { member.GroupId, member.UserId, member.JoinedAt, user.UserName, user.AvatarFileName };
        var page = await (from visible in access.MemberGroups(actor).Where(item => item.Id == groupId)
                          join member in eligible.Take(limit + 1) on visible.Id equals member.GroupId into members
                          from member in members.DefaultIfEmpty()
                          orderby member.UserId
                          select new { UserId = (int?)member.UserId, member.UserName, member.AvatarFileName,
                              JoinedAt = (DateTime?)member.JoinedAt, visible.OwnerId }).ToListAsync(ct);
        if (page.Count == 0) throw MissingGroup();
        var items = page.Where(row => row.UserId.HasValue).Take(limit)
            .Select(row => new GroupMemberDto(row.UserId!.Value, row.UserName!, AvatarUrls.FromFile(row.AvatarFileName),
                row.JoinedAt!.Value, row.UserId == row.OwnerId)).ToArray();
        return new(items, page.Count > limit ? items[^1].UserId : null);
    }

    public async Task<GroupPeoplePageDto<GroupJoinRequestDto>> ListRequestsAsync(int groupId, int actor, int cursor, int limit, CancellationToken ct)
    {
        var eligible = from request in db.GroupJoinRequests.AsNoTracking()
                       join user in PrivacyService.UnblockedUsers(db, actor) on request.UserId equals user.Id
                       where request.GroupId == groupId && request.Id > cursor && request.Status == GroupJoinRequestStatus.Pending && user.EmailConfirmed
                       orderby request.Id
                       select new { request.GroupId, request.Id, request.UserId, request.CreatedAt, user.UserName, user.AvatarFileName };
        var page = await (from owned in access.OwnedGroups(actor).Where(item => item.Id == groupId)
                          join request in eligible.Take(limit + 1) on owned.Id equals request.GroupId into requests
                          from request in requests.DefaultIfEmpty()
                          orderby request.Id
                          select new { Id = (int?)request.Id, UserId = (int?)request.UserId, request.UserName,
                              request.AvatarFileName, CreatedAt = (DateTime?)request.CreatedAt }).ToListAsync(ct);
        if (page.Count == 0) throw MissingGroup();
        var items = page.Where(row => row.Id.HasValue).Take(limit)
            .Select(row => new GroupJoinRequestDto(row.Id!.Value, row.UserId!.Value, row.UserName!,
                AvatarUrls.FromFile(row.AvatarFileName), row.CreatedAt!.Value)).ToArray();
        return new(items, page.Count > limit ? items[^1].Id : null);
    }

    private Task<T> WriteAsync<T>(int groupId, Func<SocialGroup, Task<T>> action, CancellationToken ct, int? revokedUser = null)
        => chat.InGroupAsync(groupId, async () =>
        {
            var result = await RunTransactionAsync(groupId, action, ct);
            if (revokedUser is { } userId) await chat.RevokeAsync(groupId, userId);
            return result;
        }, ct);

    private Task<T> RunTransactionAsync<T>(int groupId, Func<SocialGroup, Task<T>> action, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Acquire the group row lock before reading membership or request state.
                await access.ActiveGroups().Where(group => group.Id == groupId)
                    .ExecuteUpdateAsync(update => update.SetProperty(group => group.Privacy, group => group.Privacy), ct);
                var group = await access.ActiveGroups().SingleOrDefaultAsync(group => group.Id == groupId, ct)
                    ?? throw MissingGroup();
                var result = await action(group);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                db.ChangeTracker.Clear();
                throw;
            }
        });

    private Task<GroupMember?> FindMemberAsync(int groupId, int userId, CancellationToken ct)
        => db.GroupMembers.SingleOrDefaultAsync(member => member.GroupId == groupId && member.UserId == userId, ct);

    private Task<GroupJoinRequest?> PendingAsync(int groupId, int userId, CancellationToken ct)
        => db.GroupJoinRequests.SingleOrDefaultAsync(request => request.GroupId == groupId && request.PendingUserId == userId, ct);

    private void ActivateMember(int groupId, int userId, GroupMember? member, DateTime now)
    {
        if (member is null)
            db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId, JoinedAt = now });
        else if (member.RemovedAt is not null)
        {
            member.RemovedAt = null;
            member.JoinedAt = now;
        }
    }

    private void Record<T>(int groupId, T change, DateTime now) where T : IDomainEvent
        => OutboxEvents.Enqueue(db, groupId, change, now);

    private static void RequireOwner(SocialGroup group, int actor)
    {
        if (group.OwnerId != actor) throw MissingGroup();
    }

    private static GroupMembershipException MissingGroup() => new(404, "group_not_found", "El grupo no está disponible.");
    private static GroupMembershipException OwnerMembership() => new(409, "group_owner_required", "El propietario debe permanecer en el grupo.");
}

internal sealed class GroupMembershipException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
