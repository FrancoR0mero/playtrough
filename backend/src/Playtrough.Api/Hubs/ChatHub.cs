using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Playtrough.Api.Dtos.Groups;

namespace Playtrough.Api.Hubs;

public sealed class ChatHub(ChatMessageService messages, ChatConnections connections) : Hub
{
    public const string Path = "/hubs/chat";

    public Task<IReadOnlyList<ChatParticipantDto>> JoinGroup(int groupId)
        => messages.JoinAsync(groupId, UserId, Context.ConnectionId, Context.ConnectionAborted);

    public Task<IReadOnlyList<ChatParticipantDto>?> GetPresence(int groupId)
        => messages.ReadPresenceAsync(groupId, UserId, Context.ConnectionId, Context.ConnectionAborted);

    public Task SetTyping(int groupId, bool typing)
        => messages.SetTypingAsync(groupId, UserId, Context.ConnectionId, typing, Context.ConnectionAborted);

    public Task LeaveGroup(int groupId) => connections.InGroupAsync(groupId, () =>
    {
        connections.Leave(Context.ConnectionId, groupId, UserId);
        return Task.FromResult(true);
    }, Context.ConnectionAborted);

    public Task<ChatMessageDto> SendMessage(int groupId, Guid clientMessageId, string? text)
        => messages.SendAsync(groupId, UserId, Context.ConnectionId, clientMessageId, text, Context.ConnectionAborted);

    public Task AcknowledgeMessages(int groupId, string[] ids)
    {
        if (ids is null || ids.Length is < 1 or > 100) throw new HubException("chat_invalid_receipts");
        var messageIds = new HashSet<long>();
        foreach (var id in ids)
        {
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new HubException("chat_invalid_receipts");
            messageIds.Add(value);
        }
        return messages.AcknowledgeAsync(groupId, UserId, Context.ConnectionId, messageIds.ToArray(), Context.ConnectionAborted);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private int UserId => int.Parse(Context.UserIdentifier!, CultureInfo.InvariantCulture);
}

internal sealed class JwtHubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => GetUserId(connection.User);

    internal static string? GetUserId(ClaimsPrincipal? user)
        => int.TryParse(user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id.ToString(CultureInfo.InvariantCulture) : null;
}
