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
                        Record(new ChatMessageCreated(groupId, message.Id), message.CreatedAt);
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

    internal Task<ChatMessageStateDto?> RemoveAsync(int groupId, long messageId, int actor, CancellationToken ct)
        => connections.InGroupAsync(groupId, () => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var allowed = db.GroupMessages.Where(message => message.GroupId == groupId && message.Id == messageId
                && access.MemberGroups(actor).Any(group => group.Id == groupId &&
                    (group.OwnerId == actor || message.AuthorId == actor)));
            var now = clock.GetUtcNow().UtcDateTime;
            var changed = await allowed.Where(message => message.RemovedAt == null).ExecuteUpdateAsync(update => update
                .SetProperty(message => message.RemovedAt, now)
                .SetProperty(message => message.RemovedByUserId, actor), ct);
            var removedAt = await allowed.Select(message => message.RemovedAt).SingleOrDefaultAsync(ct);
            if (removedAt is null) return null;
            if (changed == 1)
            {
                var eventRow = Record(new ChatMessageRemoved(groupId, messageId), removedAt.Value);
                try { await db.SaveChangesAsync(ct); }
                finally { db.Entry(eventRow).State = EntityState.Detached; }
            }
            await transaction.CommitAsync(ct);
            return new ChatMessageStateDto(messageId.ToString(CultureInfo.InvariantCulture), removedAt);
        }), ct);

    private OutboxMessage Record<T>(T change, DateTime occurredAt) where T : ChatMessageReference, IDomainEvent
    {
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(), Type = typeof(T).Name, AggregateId = change.GroupId.ToString(CultureInfo.InvariantCulture),
            Payload = JsonSerializer.Serialize(change, EventJson), OccurredAt = occurredAt,
        };
        db.OutboxMessages.Add(row);
        return row;
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
        => new(message.Id.ToString(CultureInfo.InvariantCulture), message.GroupId, message.AuthorId, username,
            message.RemovedAt.HasValue ? null : message.Text, message.CreatedAt, message.RemovedAt);
}

internal record ChatMessageReference(int GroupId, long MessageId);
internal sealed record ChatMessageCreated(int GroupId, long MessageId) : ChatMessageReference(GroupId, MessageId), IDomainEvent;
internal sealed record ChatMessageRemoved(int GroupId, long MessageId) : ChatMessageReference(GroupId, MessageId), IDomainEvent;
