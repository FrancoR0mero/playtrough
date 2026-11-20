using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Hubs;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class ChatLiveTests
{
    [Theory]
    [InlineData(ChatHub.Path)]
    [InlineData(NotificationHub.Path)]
    public async Task BrowserNegotiationPreflightAllowsSignalRHeadersOnlyForConfiguredOrigins(string path)
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, path + "/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-requested-with,x-signalr-user-agent");
        using var preflight = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal("http://localhost:4200", Assert.Single(preflight.Headers.GetValues("Access-Control-Allow-Origin")));
        var headers = string.Join(",", preflight.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("X-Requested-With", headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-SignalR-User-Agent", headers, StringComparison.OrdinalIgnoreCase);
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Credentials"));

        using var other = new HttpRequestMessage(HttpMethod.Options, path + "/negotiate");
        other.Headers.Add("Origin", "https://unlisted.example");
        other.Headers.Add("Access-Control-Request-Method", "POST");
        using var disallowed = await client.SendAsync(other);
        Assert.False(disallowed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void GlobalQuotaIsSharedAcrossAccounts()
    {
        using var limits = new ChatSendLimits(Options.Create(new ChatSettings { MessagesPerUser = 1, GlobalMessages = 2 }));
        Assert.True(limits.TryAcquire(1));
        Assert.False(limits.TryAcquire(1));
        Assert.True(limits.TryAcquire(2));
        Assert.False(limits.TryAcquire(3));
    }

    [Fact]
    public async Task MembersSendOnceAndDisconnectingMembershipRevokesBothOpenTabs()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var member = await context.AuthenticatedClientAsync();
        var groupId = await CreateGroup(owner);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(22));
        using var author = await Socket.OpenAsync(context, owner, deadline.Token);
        using var reader = await Socket.OpenAsync(context, member, deadline.Token);
        using var otherTab = await Socket.OpenAsync(context, member, deadline.Token);
        Assert.Contains("chat_not_available", (await reader.InvokeAsync("JoinGroup", groupId)).GetProperty("error").GetString());
        using var joined = await member.PutAsync($"/api/groups/{groupId}/membership", null);
        joined.EnsureSuccessStatusCode();
        await author.JoinAsync(groupId);
        await reader.JoinAsync(groupId);
        await otherTab.JoinAsync(groupId);

        var clientId = Guid.NewGuid();
        var sent = (await author.InvokeAsync("SendMessage", groupId, clientId, "  Hola 🎮  ")).GetProperty("result");
        Assert.Equal("Hola 🎮", sent.GetProperty("text").GetString());
        var sentId = sent.GetProperty("id").GetString();
        Assert.Equal(sentId, (await reader.EventAsync("MessageCreated"))[0].GetProperty("id").GetString());
        var replay = (await author.InvokeAsync("SendMessage", groupId, clientId, "Hola 🎮")).GetProperty("result");
        Assert.Equal(sentId, replay.GetProperty("id").GetString());
        Assert.Contains("chat_message_id_conflict",
            (await author.InvokeAsync("SendMessage", groupId, clientId, "Otro")).GetProperty("error").GetString());

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.GroupMessages.ToListAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(item => item.Type == nameof(ChatMessageCreated)));
        using var left = await member.DeleteAsync($"/api/groups/{groupId}/membership");
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(groupId, (await reader.EventAsync("ChatAccessRevoked"))[0].GetInt32());
        Assert.Equal(groupId, (await otherTab.EventAsync("ChatAccessRevoked"))[0].GetInt32());
        Assert.Contains("chat_not_available", (await otherTab.InvokeAsync(
            "SendMessage", groupId, Guid.NewGuid(), "sin acceso")).GetProperty("error").GetString());
        using var rejoined = await member.PutAsync($"/api/groups/{groupId}/membership", null);
        rejoined.EnsureSuccessStatusCode();
        Assert.Contains("chat_not_available", (await reader.InvokeAsync(
            "SendMessage", groupId, Guid.NewGuid(), "requiere volver a suscribirse")).GetProperty("error").GetString());
        await reader.JoinAsync(groupId);
        Assert.False((await reader.InvokeAsync("LeaveGroup", groupId)).TryGetProperty("error", out _));
        Assert.Contains("chat_not_available", (await reader.InvokeAsync(
            "SendMessage", groupId, Guid.NewGuid(), "suscripción cerrada")).GetProperty("error").GetString());
    }

    [Fact]
    public async Task AFailedOutboxInsertRollsBackTheMessageAndAllowsTheSameSendIdToRetry()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        var groupId = await CreateGroup(owner);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(17));
        using var sender = await Socket.OpenAsync(context, owner, deadline.Token);
        await sender.JoinAsync(groupId);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_chat_event BEFORE INSERT ON OutboxMessages
            WHEN NEW.Type = 'ChatMessageCreated' BEGIN SELECT RAISE(ABORT, 'test'); END;
            """);
        var clientId = Guid.NewGuid();
        Assert.True((await sender.InvokeAsync("SendMessage", groupId, clientId, "prueba")).TryGetProperty("error", out _));
        Assert.False(await db.GroupMessages.AnyAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_chat_event");
        Assert.True((await sender.InvokeAsync("SendMessage", groupId, clientId, "prueba")).TryGetProperty("result", out _));
        Assert.Equal(1, await db.GroupMessages.CountAsync());
    }

    [Fact]
    public async Task DirectCallsShareQuotaAcrossTabsAndCheckUnicodeLength()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        var groupId = await CreateGroup(owner);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(17));
        using var first = await Socket.OpenAsync(context, owner, deadline.Token);
        using var second = await Socket.OpenAsync(context, owner, deadline.Token);
        await first.JoinAsync(groupId);
        await second.JoinAsync(groupId);
        Assert.Contains("chat_invalid_message", (await first.InvokeAsync(
            "SendMessage", groupId, Guid.NewGuid(), new string('x', 2001))).GetProperty("error").GetString());
        var text = string.Concat(Enumerable.Repeat("🎮", 2000));
        Assert.Equal(text, (await first.InvokeAsync("SendMessage", groupId, Guid.NewGuid(), text))
            .GetProperty("result").GetProperty("text").GetString());
        for (var i = 0; i < 9; i++)
            Assert.True((await first.InvokeAsync("SendMessage", groupId, Guid.NewGuid(), "válido")).TryGetProperty("result", out _));
        Assert.Contains("chat_rate_limited", (await second.InvokeAsync(
            "SendMessage", groupId, Guid.NewGuid(), "más")).GetProperty("error").GetString());
    }

    [Fact]
    public async Task DeliveryFiltersBothBlockDirectionsAndGroupDeletionRevokesConnections()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var member = await context.AuthenticatedClientAsync();
        var groupId = await CreateGroup(owner);
        using var joined = await member.PutAsync($"/api/groups/{groupId}/membership", null);
        joined.EnsureSuccessStatusCode();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var first = await Socket.OpenAsync(context, owner, deadline.Token);
        using var second = await Socket.OpenAsync(context, member, deadline.Token);
        await first.JoinAsync(groupId);
        await second.JoinAsync(groupId);
        using var blocked = await member.PostAsJsonAsync("/api/blocks", new { username = "games_user_1" });
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);

        await AssertNoDeliveryAsync(first, second);
        await AssertNoDeliveryAsync(second, first);
        var roster = (await owner.GetFromJsonAsync<GroupPeoplePageDto<GroupMemberDto>>($"/api/groups/{groupId}/members"))!;
        Assert.Single(roster.Items);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var memberId = await db.Users.Where(user => user.UserName == "games_user_2").Select(user => user.Id).SingleAsync();
        using var expelled = await owner.DeleteAsync($"/api/groups/{groupId}/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, expelled.StatusCode);
        Assert.Equal(groupId, (await second.EventAsync("ChatAccessRevoked"))[0].GetInt32());
        using var group = await owner.GetAsync($"/api/groups/{groupId}");
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, $"/api/groups/{groupId}");
        deletion.Headers.IfMatch.Add(group.Headers.ETag!);
        using var deleted = await owner.SendAsync(deletion);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(groupId, (await first.EventAsync("ChatAccessRevoked"))[0].GetInt32());

        async Task AssertNoDeliveryAsync(Socket sender, Socket recipient)
        {
            var sent = (await sender.InvokeAsync("SendMessage", groupId, Guid.NewGuid(), "bloqueo")).GetProperty("result");
            Assert.Equal(sent.GetProperty("id").GetString(), (await sender.EventAsync("MessageCreated"))[0].GetProperty("id").GetString());
            await recipient.JoinAsync(groupId);
            Assert.DoesNotContain(recipient.Events, entry => entry.GetProperty("target").GetString() == "MessageCreated");
        }
    }

    private static async Task<int> CreateGroup(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/groups",
            new { name = "Chat", description = "Prueba de chat", topic = "Retro", privacy = "public" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!.Id;
    }

    [Fact]
    public async Task OnlyAuthorOrOwnerCanRemoveAndTheEventAndHistoryRedactTheText()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var author = await context.AuthenticatedClientAsync();
        using var reader = await context.AuthenticatedClientAsync();
        var group = await CreateGroup(owner);
        var otherGroup = await CreateGroup(owner);
        using var joinedAuthor = await author.PutAsync($"/api/groups/{group}/membership", null);
        using var joinedReader = await reader.PutAsync($"/api/groups/{group}/membership", null);
        joinedAuthor.EnsureSuccessStatusCode();
        joinedReader.EnsureSuccessStatusCode();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(23));
        using var writer = await Socket.OpenAsync(context, author, deadline.Token);
        using var recipient = await Socket.OpenAsync(context, reader, deadline.Token);
        await writer.JoinAsync(group);
        await recipient.JoinAsync(group);
        const string text = "Texto que será retirado";
        var sendId = Guid.NewGuid();
        var sent = (await writer.InvokeAsync("SendMessage", group, sendId, text)).GetProperty("result");
        var id = sent.GetProperty("id").GetString()!;
        Assert.Equal(text, (await recipient.EventAsync("MessageCreated"))[0].GetProperty("text").GetString());
        using var forbidden = await reader.DeleteAsync($"/api/groups/{group}/messages/{id}");
        using var wrongGroup = await owner.DeleteAsync($"/api/groups/{otherGroup}/messages/{id}");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongGroup.StatusCode);

        var removed = await RemoveAsync(owner, group, id);
        Assert.NotNull(removed.RemovedAt);
        Assert.Equal(removed, await RemoveAsync(author, group, id));
        var notice = (await recipient.EventAsync("MessageRemoved"))[0].Deserialize<ChatMessageDto>(JsonSerializerOptions.Web)!;
        Assert.Equal((id, removed.RemovedAt), (notice.Id, notice.RemovedAt));
        Assert.Null(notice.Text);
        var replay = (await writer.InvokeAsync("SendMessage", group, sendId, text)).GetProperty("result");
        Assert.Equal(id, replay.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, replay.GetProperty("text").ValueKind);
        var page = (await reader.GetFromJsonAsync<ChatHistoryPageDto>($"/api/groups/{group}/messages"))!;
        var fromHistory = Assert.Single(page.Items);
        Assert.Equal(notice, fromHistory);
        var state = (await reader.GetFromJsonAsync<ChatMessageStateDto[]>($"/api/groups/{group}/messages/state?ids={id}&ids=9223372036854775807"))!;
        Assert.Equal(removed, Assert.Single(state));
        using var blocked = await reader.PostAsJsonAsync("/api/blocks", new { username = "games_user_2" });
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.Empty((await reader.GetFromJsonAsync<ChatMessageStateDto[]>($"/api/groups/{group}/messages/state?ids={id}"))!);
        using var left = await reader.DeleteAsync($"/api/groups/{group}/membership");
        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        using var lost = await reader.GetAsync($"/api/groups/{group}/messages/state?ids={id}");
        Assert.Equal(HttpStatusCode.NotFound, lost.StatusCode);

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var archived = await db.GroupMessages.AsNoTracking().SingleAsync();
        var ownerId = await db.Users.Where(user => user.UserName == "games_user_1").Select(user => user.Id).SingleAsync();
        Assert.Equal(text, archived.Text);
        Assert.Equal(ownerId, archived.RemovedByUserId);
        var item = await db.OutboxMessages.SingleAsync(row => row.Type == nameof(ChatMessageRemoved));
        Assert.DoesNotContain(text, item.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentRemovalsAreIdempotentAndAnOutboxFailureRollsBackTheRemoval()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        var group = await CreateGroup(owner);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(19));
        using var sender = await Socket.OpenAsync(context, owner, deadline.Token);
        await sender.JoinAsync(group);
        var message = (await sender.InvokeAsync("SendMessage", group, Guid.NewGuid(), "mensaje")).GetProperty("result");
        var id = message.GetProperty("id").GetString()!;
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_chat_removal BEFORE INSERT ON OutboxMessages
            WHEN NEW.Type = 'ChatMessageRemoved' BEGIN SELECT RAISE(ABORT, 'test'); END;
            """);
        using var failed = await owner.DeleteAsync($"/api/groups/{group}/messages/{id}");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Null((await db.GroupMessages.AsNoTracking().SingleAsync()).RemovedAt);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_chat_removal");
        var removals = await Task.WhenAll(RemoveAsync(owner, group, id), RemoveAsync(owner, group, id));
        Assert.NotNull(removals[0].RemovedAt);
        Assert.Equal(removals[0], removals[1]);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(row => row.Type == nameof(ChatMessageRemoved)));
    }

    [Fact]
    public async Task DelayedCreationNeverBroadcastsTextOnceRemovalIsCommitted()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        var group = await CreateGroup(owner);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var sender = await Socket.OpenAsync(context, owner, deadline.Token);
        await sender.JoinAsync(group);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER delay_chat_creation AFTER INSERT ON OutboxMessages
            WHEN NEW.Type = 'ChatMessageCreated' BEGIN
                UPDATE OutboxMessages SET NextAttemptAt = '2099-01-01 00:00:00' WHERE Id = NEW.Id;
            END;
            """);
        var sent = (await sender.InvokeAsync("SendMessage", group, Guid.NewGuid(), "solo en el archivo")).GetProperty("result");
        var id = sent.GetProperty("id").GetString()!;
        await RemoveAsync(owner, group, id);
        var removed = (await sender.EventAsync("MessageRemoved"))[0];
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("text").ValueKind);
        var created = db.OutboxMessages.AsNoTracking().Where(row => row.Type == nameof(ChatMessageCreated));
        await created.ExecuteUpdateAsync(update => update.SetProperty(row => row.NextAttemptAt, (DateTime?)null));
        while (!await created.AnyAsync(row => row.ProcessedAt != null, deadline.Token))
            await Task.Delay(30, deadline.Token);
        await sender.JoinAsync(group);
        Assert.DoesNotContain(sender.Events, item => item.GetProperty("target").GetString() == "MessageCreated");
    }

    private static async Task<ChatMessageStateDto> RemoveAsync(HttpClient client, int group, string id)
    {
        using var response = await client.DeleteAsync($"/api/groups/{group}/messages/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatMessageStateDto>())!;
    }

    internal sealed class Socket(WebSocket transport, CancellationToken ct) : IDisposable
    {
        private const char Separator = '\u001e';
        private readonly Queue<JsonElement> frames = new();
        private readonly List<JsonElement> events = [];
        private int invocation;
        internal IReadOnlyList<JsonElement> Events => events;

        internal static async Task<Socket> OpenAsync(GamesApiTestContext context, HttpClient user, CancellationToken ct,
            string path = ChatHub.Path)
        {
            var socket = await context.Factory.Server.CreateWebSocketClient().ConnectAsync(new Uri(
                "wss://localhost" + path + "?access_token=" + user.DefaultRequestHeaders.Authorization!.Parameter), ct);
            var connection = new Socket(socket, ct);
            try
            {
                await connection.WriteAsync(new { protocol = "json", version = 1 });
                Assert.False((await connection.ReadAsync()).TryGetProperty("error", out _));
                return connection;
            }
            catch { socket.Dispose(); throw; }
        }

        internal async Task JoinAsync(int groupId) => Assert.False((await InvokeAsync("JoinGroup", groupId)).TryGetProperty("error", out _));

        internal async Task<JsonElement> InvokeAsync(string target, params object[] arguments)
        {
            var invocationId = (++invocation).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WriteAsync(new { type = 1, invocationId, target, arguments });
            while (true)
            {
                var frame = await ReadAsync();
                var type = frame.GetProperty("type").GetInt32();
                if (type == 3 && frame.GetProperty("invocationId").GetString() == invocationId) return frame;
                if (type == 1) events.Add(frame);
            }
        }

        internal async Task<JsonElement> EventAsync(string target)
        {
            while (true)
            {
                var index = events.FindIndex(item => item.GetProperty("target").GetString() == target);
                if (index >= 0)
                {
                    var notification = events[index];
                    events.RemoveAt(index);
                    return notification.GetProperty("arguments");
                }
                var frame = await ReadAsync();
                if (frame.GetProperty("type").GetInt32() == 1) events.Add(frame);
            }
        }

        private Task WriteAsync<T>(T message) => transport.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + Separator)),
            WebSocketMessageType.Text, true, ct);

        private async Task<JsonElement> ReadAsync()
        {
            while (frames.Count == 0)
            {
                using var bytes = new MemoryStream();
                var buffer = new byte[4096];
                WebSocketReceiveResult part;
                do
                {
                    part = await transport.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    Assert.Equal(WebSocketMessageType.Text, part.MessageType);
                    bytes.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                foreach (var frame in Encoding.UTF8.GetString(bytes.ToArray()).Split(Separator, StringSplitOptions.RemoveEmptyEntries))
                {
                    using var parsed = JsonDocument.Parse(frame);
                    frames.Enqueue(parsed.RootElement.Clone());
                }
            }
            return frames.Dequeue();
        }

        public void Dispose() => transport.Dispose();
    }
}
