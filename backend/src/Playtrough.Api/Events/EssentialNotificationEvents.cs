namespace Playtrough.Api.Events;

internal interface IEssentialNotificationEvent : IDomainEvent
{
    int RecipientId { get; }
}

internal sealed record ModerationDecisionApplied(int RecipientId) : IEssentialNotificationEvent;
internal sealed record ModerationAppealResolved(int RecipientId) : IEssentialNotificationEvent;
internal sealed record SubscriptionActivated(int RecipientId) : IEssentialNotificationEvent;
internal sealed record PaymentFailed(int RecipientId) : IEssentialNotificationEvent;
internal sealed record SubscriptionExpired(int RecipientId) : IEssentialNotificationEvent;
