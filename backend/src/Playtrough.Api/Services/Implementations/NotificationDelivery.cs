using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Playtrough.Api.Hubs;

namespace Playtrough.Api.Services.Implementations;

internal sealed class NotificationDelivery(NotificationStore store, NotificationPreferences preferences, IHubContext<NotificationHub> hub)
{
    // The worker acknowledges the domain event only after this method succeeds.
    internal async Task PublishAsync(Guid eventId, NotificationCategory category, NotificationDraft draft, CancellationToken ct)
    {
        if (!await preferences.IsEnabledAsync(draft.RecipientId, category, ct)) return;
        var notification = await store.PersistAsync(eventId, draft, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await hub.Clients.User(notification.RecipientId.ToString(CultureInfo.InvariantCulture))
            .SendAsync("NotificationAvailable", notification.Id.ToString(CultureInfo.InvariantCulture), timeout.Token);
    }
}
