using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Hubs;

public sealed class ChatMessageService(AppDbContext db, GroupAccess access, ChatConnections connections,
    ChatSendLimits limits, TimeProvider clock)
{
    internal static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

    internal Task JoinAsync(int groupId, int user, string connection, CancellationToken ct)
        => connections.InGroupAsync(groupId, async () =>
        {
            await RequireMembership(groupId, user, ct);
            ct.ThrowIfCancellationRequested();
            if (!connections.Subscribe(connection, groupId, user)) throw new HubException("chat_already_subscribed");
            return true;
        }, ct);

    internal Task<ChatMessageDto> SendAsync(int groupId, int user, string connection, Guid clientId, string? text, CancellationToken ct)
    {
        if (!connections.IsSubscribed(connection, groupId, user)) throw new HubException("chat_not_available");
        var clean = PlainText.Sanitize(text);
        if (clientId == Guid.Empty || clean is null || clean.EnumerateRunes().Count() > GroupMessage.MaxTextLength)
            throw new HubException("chat_invalid_message");
        if (!limits.TryAcquire(user)) throw new HubException("chat_rate_limited");

        return connections.InGroupAsync(groupId, async () =>
        {
            if (!connections.IsSubscribed(connection, groupId, user)) throw new HubException("chat_not_available");
            await RequireMembership(groupId, user, ct);
            var username = await db.Users.Where(item => item.Id == user).Select(item => item.UserName).SingleAsync(ct);
            var existing = await FindAsync(user, clientId, ct);
            if (existing is not null) return Replay(existing, groupId, clean, username!);
            try
            {
                var sent = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    var now = clock.GetUtcNow().UtcDateTime;
                    var message = new GroupMessage
                    {
                        GroupId = groupId, AuthorId = user, ClientMessageId = clientId, Text = clean,
                        CreatedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc),
                    };
                    try
                    {
                        db.GroupMessages.Add(message);
                        await db.SaveChangesAsync(ct);
                        db.OutboxMessages.Add(new OutboxMessage
                        {
                            Id = Guid.NewGuid(), Type = nameof(ChatMessageCreated), AggregateId = groupId.ToString(CultureInfo.InvariantCulture),
                            Payload = JsonSerializer.Serialize(new ChatMessageCreated(groupId, message.Id), EventJson), OccurredAt = message.CreatedAt,
                        });
                        await db.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);
                        return message;
                    }
                    catch
                    {
                        db.ChangeTracker.Clear();
                        throw;
                    }
                });
                return ToDto(sent, username!);
            }
            catch (DbUpdateException)
            {
                // A connection for another group can race on the author/client UUID.
                var winner = await FindAsync(user, clientId, ct);
                if (winner is null) throw;
                return Replay(winner, groupId, clean, username!);
            }
        }, ct);
    }

    private async Task RequireMembership(int groupId, int user, CancellationToken ct)
    {
        if (!await access.MemberGroups(user).AnyAsync(group => group.Id == groupId, ct))
            throw new HubException("chat_not_available");
    }

    private Task<GroupMessage?> FindAsync(int author, Guid clientId, CancellationToken ct)
        => db.GroupMessages.AsNoTracking().SingleOrDefaultAsync(item => item.AuthorId == author && item.ClientMessageId == clientId, ct);

    private static ChatMessageDto Replay(GroupMessage message, int groupId, string text, string username)
        => message.GroupId == groupId && string.Equals(message.Text, text, StringComparison.Ordinal)
            ? ToDto(message, username) : throw new HubException("chat_message_id_conflict");

    internal static ChatMessageDto ToDto(GroupMessage message, string username)
        => new(message.Id.ToString(CultureInfo.InvariantCulture), message.GroupId, message.AuthorId, username, message.Text, message.CreatedAt);
}

internal sealed record ChatMessageCreated(int GroupId, long MessageId) : IDomainEvent;
