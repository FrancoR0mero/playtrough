using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Events;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

internal sealed class SocialNotificationHandler<T>(SocialNotifications notifications) : IOutboxHandler where T : class, IDomainEvent
{
    public string EventType => typeof(T).Name;

    public Task HandleAsync(OutboxMessage message, CancellationToken ct)
        => notifications.PublishAsync(message.Id, JsonSerializer.Deserialize<T>(message.Payload, OutboxEvents.Json)
            ?? throw new InvalidOperationException("Invalid social notification event."), ct);
}

internal sealed class SocialNotifications(AppDbContext db, TimelineEntryAccess timeline, GroupAccess groups, NotificationDelivery delivery)
{
    internal const string FriendRequest = "friend-request";
    internal const string TimelineComment = "timeline-comment";
    internal const string GroupApproved = "GroupJoinApproved";
    internal const string GroupRejected = "GroupJoinRejected";

    internal static IQueryable<FriendRequest> PendingFriends(AppDbContext db)
        => db.FriendRequests.AsNoTracking().Where(request => request.Status == FriendRequestStatus.Pending);

    internal static IQueryable<FriendRequest> AcceptedFriends(AppDbContext db)
        => db.FriendRequests.AsNoTracking().Where(request => request.Status == FriendRequestStatus.Accepted &&
            db.Friendships.Any(friend => friend.UserLowId == request.SenderId && friend.UserHighId == request.ReceiverId
                || friend.UserHighId == request.SenderId && friend.UserLowId == request.ReceiverId));

    internal static IQueryable<TimelineEntryComment> VisibleComments(AppDbContext db, TimelineEntryAccess timeline, int user)
        => from comment in db.TimelineEntryComments.AsNoTracking()
           join entry in timeline.VisibleTo(user).Where(entry => !entry.IsHidden) on comment.EntryId equals entry.Id
           select comment;

    internal async Task PublishAsync(Guid eventId, IDomainEvent change, CancellationToken ct)
    {
        var candidate = change switch
        {
            FriendRequestSent item => (NotificationCategory.Friends, new NotificationDraft(item.ReceiverId, item.SenderId,
                nameof(FriendRequestSent), FriendRequest, item.RequestId)),
            FriendRequestAccepted item => (NotificationCategory.Friends, new NotificationDraft(item.SenderId, item.ReceiverId,
                nameof(FriendRequestAccepted), FriendRequest, item.RequestId)),
            TimelineEntryLiked item => (NotificationCategory.Interactions, new NotificationDraft(item.OwnerId, item.ActorId,
                nameof(TimelineEntryLiked), NotificationCenter.TimelineEntry, item.EntryId)),
            TimelineEntryCommented item => (NotificationCategory.Interactions, new NotificationDraft(item.OwnerId, item.ActorId,
                nameof(TimelineEntryCommented), TimelineComment, item.CommentId)),
            GroupJoinRequested item => (NotificationCategory.Groups, new NotificationDraft(item.OwnerId, item.UserId,
                nameof(GroupJoinRequested), NotificationCenter.Group, item.GroupId)),
            GroupJoinResolved item => (NotificationCategory.Groups, new NotificationDraft(item.UserId, item.OwnerId,
                item.Approved ? GroupApproved : GroupRejected, NotificationCenter.Group, item.GroupId)),
            GroupMemberRemoved item => (NotificationCategory.Groups, new NotificationDraft(item.UserId, item.OwnerId,
                nameof(GroupMemberRemoved), NotificationCenter.Group, item.GroupId)),
            _ => throw new InvalidOperationException("Unknown social notification event."),
        };
        var (category, draft) = candidate;
        if (draft.RecipientId == draft.ActorId || !await PrivacyService.UnblockedUsers(db, draft.RecipientId)
            .AnyAsync(user => user.Id == draft.ActorId && user.EmailConfirmed, ct)) return;
        if (await IsRelevantAsync(change, ct)) await delivery.PublishAsync(eventId, category, draft, ct);
    }

    private Task<bool> IsRelevantAsync(IDomainEvent change, CancellationToken ct) => change switch
    {
        FriendRequestSent item => PendingFriends(db).AnyAsync(request => request.Id == item.RequestId
            && request.SenderId == item.SenderId && request.ReceiverId == item.ReceiverId, ct),
        FriendRequestAccepted item => AcceptedFriends(db).AnyAsync(request => request.Id == item.RequestId
            && request.SenderId == item.SenderId && request.ReceiverId == item.ReceiverId, ct),
        TimelineEntryLiked item => timeline.VisibleTo(item.OwnerId).AnyAsync(entry => entry.Id == item.EntryId
            && entry.UserId == item.OwnerId && !entry.IsHidden, ct),
        TimelineEntryCommented item => VisibleComments(db, timeline, item.OwnerId).AnyAsync(comment => comment.Id == item.CommentId
            && comment.EntryId == item.EntryId && comment.AuthorId == item.ActorId
            && db.TimelineEntries.Any(entry => entry.Id == item.EntryId && entry.UserId == item.OwnerId && !entry.IsHidden), ct),
        GroupJoinRequested item => groups.OwnedGroups(item.OwnerId).AnyAsync(group => group.Id == item.GroupId &&
            db.GroupJoinRequests.Any(request => request.GroupId == group.Id && request.Id == item.RequestId
                && request.UserId == item.UserId && request.Status == GroupJoinRequestStatus.Pending), ct),
        GroupJoinResolved item => groups.OwnedGroups(item.OwnerId).AnyAsync(group => group.Id == item.GroupId &&
            db.GroupJoinRequests.Any(request => request.GroupId == group.Id && request.Id == item.RequestId
                && request.UserId == item.UserId && request.Status == (item.Approved
                    ? GroupJoinRequestStatus.Approved : GroupJoinRequestStatus.Rejected))
            && (!item.Approved || groups.ActiveMembers().Any(member => member.GroupId == group.Id && member.UserId == item.UserId)), ct),
        GroupMemberRemoved item => groups.OwnedGroups(item.OwnerId).AnyAsync(group => group.Id == item.GroupId &&
            db.GroupMembers.Any(member => member.GroupId == group.Id && member.UserId == item.UserId && member.RemovedAt != null), ct),
        _ => throw new InvalidOperationException("Unknown social notification event."),
    };
}
