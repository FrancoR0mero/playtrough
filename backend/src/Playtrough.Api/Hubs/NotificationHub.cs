using Microsoft.AspNetCore.SignalR;

namespace Playtrough.Api.Hubs;

// SignalR tracks connections by the authenticated UserIdentifier. The hub exposes no client invocations.
public sealed class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";
}
