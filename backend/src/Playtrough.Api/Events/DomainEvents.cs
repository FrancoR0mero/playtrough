namespace Playtrough.Api.Events;

public interface IDomainEvent;
public sealed record FriendRequestSent(int RequestId, int SenderId, int ReceiverId, DateTime CreatedAt) : IDomainEvent;
public sealed record FriendRequestAccepted(int RequestId, int SenderId, int ReceiverId, DateTime AcceptedAt) : IDomainEvent;

public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default);
}

// In-process publication has no subscribers in this phase. No unbounded event log
// or durable delivery is promised; notifications/feed can add handlers later.
public sealed class InMemoryDomainEventPublisher : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default) => Task.CompletedTask;
}
