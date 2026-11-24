using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Playtrough.Api.Hubs;

public sealed class ChatConnections(IHubContext<ChatHub> hub, ILogger<ChatConnections> logger)
{
    private readonly ConcurrentDictionary<string, Subscription> subscriptions = new();
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    internal async Task<T> InGroupAsync<T>(int groupId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = gates[(uint)groupId % (uint)gates.Length];
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    internal bool Subscribe(string connection, int groupId, int userId)
        => subscriptions.TryAdd(connection, new(groupId, userId)) || IsSubscribed(connection, groupId, userId);

    internal bool IsSubscribed(string connection, int groupId, int userId)
        => subscriptions.TryGetValue(connection, out var current) && current.GroupId == groupId && current.UserId == userId;

    internal void Leave(string connection, int groupId, int userId)
    {
        if (subscriptions.TryGetValue(connection, out var current) && current.GroupId == groupId && current.UserId == userId)
            subscriptions.TryRemove(new KeyValuePair<string, Subscription>(connection, current));
    }

    internal void Disconnect(string connection) => subscriptions.TryRemove(connection, out _);

    // Presence and typing are scoped to a live connection. Callers hold the group gate.
    internal Dictionary<int, bool> Participants(int groupId, DateTimeOffset now)
        => subscriptions.Values.Where(item => item.GroupId == groupId).GroupBy(item => item.UserId)
            .ToDictionary(user => user.Key, user => user.Any(item => item.TypingUntil > now));

    internal bool TakePresenceSnapshot(string connection, DateTimeOffset now)
    {
        if (!subscriptions.TryGetValue(connection, out var current) || current.NextSnapshot > now) return false;
        current.NextSnapshot = now.AddMilliseconds(750);
        return true;
    }

    internal bool TakeTypingUpdate(string connection, DateTimeOffset now)
    {
        if (!subscriptions.TryGetValue(connection, out var current) || current.NextTyping > now) return false;
        current.NextTyping = now.AddMilliseconds(750);
        return true;
    }

    internal void SetTyping(string connection, DateTimeOffset until)
    {
        if (subscriptions.TryGetValue(connection, out var current)) current.TypingUntil = until;
    }

    internal string[] Destinations(int groupId, HashSet<int> eligibleUsers)
        => subscriptions.Where(item => item.Value.GroupId == groupId && eligibleUsers.Contains(item.Value.UserId))
            .Select(item => item.Key).ToArray();

    internal async Task NotifyDeliveredAsync(int groupId, int authorId, string[] ids)
    {
        var destinations = Destinations(groupId, [authorId]);
        if (destinations.Length == 0) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await hub.Clients.Clients(destinations).SendAsync("MessagesDelivered", ids, timeout.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning("Unable to notify chat delivery ({ExceptionType}).", error.GetType().Name);
        }
    }

    // The caller holds the group gate across the database change and removal of subscriptions.
    internal async Task RevokeAsync(int groupId, int? userId = null)
    {
        var revoked = new List<string>();
        foreach (var item in subscriptions)
            if (item.Value.GroupId == groupId && (userId is null || item.Value.UserId == userId)
                && subscriptions.TryRemove(item))
                revoked.Add(item.Key);
        if (revoked.Count == 0) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await hub.Clients.Clients(revoked).SendAsync("ChatAccessRevoked", groupId, timeout.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning("Unable to notify revoked chat connections ({ExceptionType}).", error.GetType().Name);
        }
    }

    private sealed class Subscription(int groupId, int userId)
    {
        internal int GroupId { get; } = groupId;
        internal int UserId { get; } = userId;
        internal DateTimeOffset TypingUntil { get; set; }
        internal DateTimeOffset NextSnapshot { get; set; }
        internal DateTimeOffset NextTyping { get; set; }
    }
}
