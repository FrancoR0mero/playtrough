using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Playtrough.Api.Data;
using Playtrough.Api.Events;
using Playtrough.Api.Hubs;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public sealed class NotificationInfrastructureTests
{
    [Fact]
    public async Task PrivateChannelTargetsBothTabsAndCannotBeChangedByTheClient()
    {
        using var context = new GamesApiTestContext();
        using var recipient = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        using var firstTab = await Open(recipient);
        using var secondTab = await Open(recipient);
        using var otherUser = await Open(other);
        Assert.True((await otherUser.InvokeAsync("Subscribe", Subject(recipient))).TryGetProperty("error", out _));
        var hub = context.Factory.Services.GetRequiredService<IHubContext<NotificationHub>>();

        await Send(recipient, "first");
        await Send(other, "own");
        Assert.Equal("first", (await firstTab.EventAsync("Probe"))[0].GetString());
        Assert.Equal("first", (await secondTab.EventAsync("Probe"))[0].GetString());
        Assert.Equal("own", (await otherUser.EventAsync("Probe"))[0].GetString());

        secondTab.Dispose();
        using var replacementTab = await Open(recipient);
        await Send(recipient, "next");
        Assert.Equal("next", (await firstTab.EventAsync("Probe"))[0].GetString());
        Assert.Equal("next", (await replacementTab.EventAsync("Probe"))[0].GetString());

        Task<ChatLiveTests.Socket> Open(HttpClient user)
            => ChatLiveTests.Socket.OpenAsync(context, user, timeout.Token, NotificationHub.Path);
        Task Send(HttpClient user, string marker)
            => hub.Clients.User(Subject(user)).SendAsync("Probe", marker, timeout.Token);
        static string Subject(HttpClient user)
            => new JwtSecurityTokenHandler().ReadJwtToken(user.DefaultRequestHeaders.Authorization!.Parameter).Subject;
    }

    [Fact]
    public async Task PersistedNotificationSurvivesADeliveryFailureAndWorkerRestart()
    {
        using var context = new ReviewTestContext();
        await using var firstDb = context.CreateDb(false);
        var item = new OutboxMessage
        {
            Id = Guid.NewGuid(), Type = TestHandler.Type, AggregateId = "42", Payload = """{"version":1,"resourceId":42}""",
            OccurredAt = context.Clock.UtcNow.UtcDateTime,
        };
        var draft = new NotificationDraft(context.UserIds[0], context.UserIds[1], "test_activity", "entry", 42);
        firstDb.OutboxMessages.Add(item);
        await firstDb.SaveChangesAsync();

        var firstHandler = new TestHandler(new NotificationStore(firstDb, context.Clock), draft, fail: true);
        await Processor(firstDb, firstHandler).ProcessBatchAsync(default);
        await using var restarted = context.CreateDb(false);
        var persisted = await restarted.Notifications.SingleAsync();
        Assert.Equal(firstHandler.NotificationId, persisted.Id);
        Assert.Equal(item.Id, persisted.EventId);
        Assert.Equal((byte)1, persisted.PayloadVersion);
        Assert.Null((await restarted.OutboxMessages.AsNoTracking().SingleAsync()).ProcessedAt);

        context.Clock.UtcNow += TimeSpan.FromSeconds(1);
        var retry = new TestHandler(new NotificationStore(restarted, context.Clock), draft, fail: false);
        await Processor(restarted, retry).ProcessBatchAsync(default);
        Assert.Equal(persisted.Id, retry.NotificationId);
        Assert.Equal(1, await restarted.Notifications.CountAsync());
        Assert.NotNull((await restarted.OutboxMessages.AsNoTracking().SingleAsync()).ProcessedAt);

        OutboxProcessor Processor(AppDbContext db, IOutboxHandler handler)
            => new(db, [handler], context.Clock, NullLogger<OutboxProcessor>.Instance);
    }

    [Fact]
    public async Task FailedPersistenceDoesNotLeaveANotificationAndDeduplicationIsScopedToRecipientAndType()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb(false);
        var store = new NotificationStore(db, context.Clock);
        var eventId = Guid.NewGuid();
        var draft = new NotificationDraft(context.UserIds[0], null, "test_activity", null, null);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => store.PersistAsync(eventId, draft with { RecipientId = int.MaxValue }, default));
        Assert.False(await db.Notifications.AnyAsync());
        var first = await store.PersistAsync(eventId, draft, default);
        Assert.Equal(first.Id, (await store.PersistAsync(eventId, draft, default)).Id);
        await store.PersistAsync(eventId, draft with { RecipientId = context.UserIds[1] }, default);
        await store.PersistAsync(eventId, draft with { Type = "test_account" }, default);
        Assert.Equal(3, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ConcurrentHandlersRecoverTheWinningNotification()
    {
        using var context = new ReviewTestContext();
        await using var reader = context.CreateDb(false);
        var gate = new ConcurrentInsertGate();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(reader.Database.GetConnectionString()!).AddInterceptors(gate).Options;
        await using var firstDb = new AppDbContext(options);
        await using var secondDb = new AppDbContext(options);
        var draft = new NotificationDraft(context.UserIds[0], null, "test_activity", null, null);
        var eventId = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));

        var first = new NotificationStore(firstDb, context.Clock).PersistAsync(eventId, draft, timeout.Token);
        var second = new NotificationStore(secondDb, context.Clock).PersistAsync(eventId, draft, timeout.Token);
        var notifications = await Task.WhenAll(first, second);

        Assert.Equal(notifications[0].Id, notifications[1].Id);
        Assert.Equal(notifications[0].Id, (await reader.Notifications.SingleAsync()).Id);
    }

    private sealed class ConcurrentInsertGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int inserts;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"Notifications\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref inserts) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class TestHandler(NotificationStore store, NotificationDraft draft, bool fail) : IOutboxHandler
    {
        internal const string Type = "TestNotificationEvent";
        public string EventType => Type;
        internal long? NotificationId { get; private set; }

        public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
        {
            NotificationId = (await store.PersistAsync(message.Id, draft, ct)).Id;
            if (fail) throw new InvalidOperationException("Delivery failed");
        }
    }
}
