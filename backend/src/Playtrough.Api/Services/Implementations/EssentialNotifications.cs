using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Events;

namespace Playtrough.Api.Services.Implementations;

internal static class EssentialNotifications
{
    internal static (NotificationCategory Category, string Message)? Describe(string type) => type switch
    {
        nameof(ModerationDecisionApplied) => (NotificationCategory.Moderation, "Se aplicó una decisión de moderación sobre tu contenido."),
        nameof(ModerationAppealResolved) => (NotificationCategory.Moderation, "Se resolvió tu apelación de moderación."),
        nameof(SubscriptionActivated) => (NotificationCategory.Account, "Tu suscripción está activa."),
        nameof(PaymentFailed) => (NotificationCategory.Account, "No pudimos procesar el pago de tu suscripción."),
        nameof(SubscriptionExpired) => (NotificationCategory.Account, "Tu suscripción venció."),
        _ => null,
    };
}

internal sealed class EssentialNotificationHandler<T>(AppDbContext db, NotificationDelivery delivery)
    : IOutboxHandler where T : class, IEssentialNotificationEvent
{
    public string EventType => typeof(T).Name;

    public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var change = JsonSerializer.Deserialize<T>(message.Payload, OutboxEvents.Json)
            ?? throw new InvalidOperationException("Invalid essential notification event.");
        if (change.RecipientId <= 0) throw new InvalidOperationException("Invalid essential notification recipient.");
        var notice = EssentialNotifications.Describe(EventType)
            ?? throw new InvalidOperationException("Unknown essential notification event.");

        if (!await db.Users.AnyAsync(user => user.Id == change.RecipientId, ct)) return;
        await delivery.PublishAsync(message.Id, notice.Category,
            new NotificationDraft(change.RecipientId, null, EventType, null, null), ct);
    }
}
