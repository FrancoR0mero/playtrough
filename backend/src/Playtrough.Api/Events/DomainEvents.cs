namespace Playtrough.Api.Events;

public interface IDomainEvent;
public sealed record FriendRequestSent(int RequestId, int SenderId, int ReceiverId, DateTime CreatedAt) : IDomainEvent;
public sealed record FriendRequestAccepted(int RequestId, int SenderId, int ReceiverId, DateTime AcceptedAt) : IDomainEvent;

public sealed record ContentReacted(string ContentType, long ContentId, int ActorId, int OwnerId,
    string? Value, string? PreviousValue) : IDomainEvent;
public sealed record TimelineEntryCommented(long EntryId, long CommentId, int ActorId, int OwnerId, DateTime CreatedAt) : IDomainEvent;
internal sealed record TimelineEntryLiked(long EntryId, int ActorId, int OwnerId) : IDomainEvent;

public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default);
}

// The in-process hook is independent of the durable notification outbox.
public sealed class InMemoryDomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default) => Task.CompletedTask;
}
