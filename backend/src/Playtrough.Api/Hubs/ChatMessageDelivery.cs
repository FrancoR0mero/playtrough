using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Events;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Hubs;

internal sealed class ChatMessageDelivery(AppDbContext db, GroupAccess access, ChatConnections connections, IHubContext<ChatHub> hub)
    : IChatOutboxHandler
{
    public string EventType => nameof(ChatMessageCreated);

    public Task HandleAsync(OutboxMessage item, CancellationToken ct)
    {
        var change = JsonSerializer.Deserialize<ChatMessageCreated>(item.Payload, ChatMessageService.EventJson)
            ?? throw new InvalidOperationException("Invalid chat event.");
        return connections.InGroupAsync(change.GroupId, async () =>
        {
            var row = await (from message in db.GroupMessages.AsNoTracking()
                             join active in access.ActiveGroups() on message.GroupId equals active.Id
                             join author in db.Users.AsNoTracking() on message.AuthorId equals author.Id
                             where message.Id == change.MessageId && message.GroupId == change.GroupId
                             select new { Message = message, author.UserName }).SingleOrDefaultAsync(ct);
            if (row is null) return false;

            var recipients = await (from member in access.ActiveMembers()
                                    join user in PrivacyService.UnblockedUsers(db, row.Message.AuthorId) on member.UserId equals user.Id
                                    where member.GroupId == change.GroupId
                                    select member.UserId).ToListAsync(ct);
            var destinations = connections.Destinations(change.GroupId, recipients.ToHashSet());
            if (destinations.Length != 0)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await hub.Clients.Clients(destinations).SendAsync("MessageCreated",
                    ChatMessageService.ToDto(row.Message, row.UserName!), timeout.Token);
            }
            return true;
        }, ct);
    }
}
