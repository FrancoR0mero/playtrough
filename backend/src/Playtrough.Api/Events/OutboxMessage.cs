namespace Playtrough.Api.Events;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

public sealed record GroupJoinRequested(int GroupId, int RequestId, int OwnerId, int UserId) : IDomainEvent;
public sealed record GroupJoinResolved(int GroupId, int RequestId, int OwnerId, int UserId, bool Approved) : IDomainEvent;
public sealed record GroupMemberRemoved(int GroupId, int OwnerId, int UserId) : IDomainEvent;
