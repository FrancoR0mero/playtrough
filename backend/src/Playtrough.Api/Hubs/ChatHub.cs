using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Playtrough.Api.Hubs;

public sealed class ChatHub : Hub
{
    public const string Path = "/hubs/chat";
}

internal sealed class JwtHubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => GetUserId(connection.User);

    internal static string? GetUserId(ClaimsPrincipal? user)
        => int.TryParse(user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id.ToString(CultureInfo.InvariantCulture) : null;
}
