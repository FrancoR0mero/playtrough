using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Events;
using Playtrough.Api.Hubs;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public sealed class NotificationCenterTests
{
    [Fact]
    public async Task PagesAndReadOperationsAreScopedAndBulkReadPreservesNewRows()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        using var anonymous = context.Factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync("/api/notifications/unread-count");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var first = Row(User(owner), now);
        var second = Row(User(owner), now);
        var otherRows = Row(User(other), now);
        db.Notifications.AddRange(first, second, otherRows);
        await db.SaveChangesAsync();

        var page = await GetPage(owner, "?limit=1");
        Assert.Equal(second.Id.ToString(), Assert.Single(page.Items).Id);
        Assert.NotNull(page.ReadCutoff);
        Assert.NotNull(page.NextCursor);
        var olderUrl = "?limit=1&cursor=" + Uri.EscapeDataString(page.NextCursor);
        var older = await GetPage(owner, olderUrl);
        Assert.Equal(first.Id.ToString(), Assert.Single(older.Items).Id);
        Assert.Null(older.NextCursor);
        Assert.Null(older.ReadCutoff);
        using var foreignPage = await other.GetAsync("/api/notifications" + olderUrl);
        Assert.Equal(HttpStatusCode.BadRequest, foreignPage.StatusCode);
        using var foreignRead = await other.PutAsync($"/api/notifications/{second.Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        using var foreignCutoff = await other.PostAsJsonAsync("/api/notifications/read-all", new { cutoff = page.ReadCutoff });
        Assert.Equal(HttpStatusCode.BadRequest, foreignCutoff.StatusCode);

        using var read = await owner.PutAsync($"/api/notifications/{second.Id}/read", null);
        using var repeated = await owner.PutAsync($"/api/notifications/{second.Id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(1, await Unread(owner));

        // New inserts can have the same timestamp or a lower timestamp if a clock drifts.
        db.Notifications.AddRange(Row(User(owner), now), Row(User(owner), now.AddSeconds(-1)));
        await db.SaveChangesAsync();
        using var bulk = await owner.PostAsJsonAsync("/api/notifications/read-all", new { cutoff = page.ReadCutoff });
        bulk.EnsureSuccessStatusCode();
        Assert.Equal(2, (await bulk.Content.ReadFromJsonAsync<NotificationCountDto>())!.UnreadCount);
        Assert.Equal(2, await Unread(owner));
        Assert.Equal(1, await Unread(other));
    }

    [Fact]
    public async Task GroupDetailsAreRedactedAfterBlockingOrLosingMembership()
    {
        using var context = new GamesApiTestContext();
        using var recipient = await context.AuthenticatedClientAsync();
        using var actor = await context.AuthenticatedClientAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var group = new SocialGroup { OwnerId = User(actor), Privacy = GroupPrivacy.Private, CreatedAt = now };
        var member = new GroupMember { Group = group, UserId = User(recipient), JoinedAt = now };
        db.GroupMembers.Add(member);
        await db.SaveChangesAsync();
        db.Notifications.Add(Row(User(recipient), now, User(actor), NotificationCenter.Group, group.Id));
        await db.SaveChangesAsync();

        Assert.Equal($"/groups/{group.Id}", Assert.Single((await GetPage(recipient)).Items).Destination);
        var block = new UserBlock { BlockerId = User(actor), BlockedId = User(recipient), CreatedAt = now };
        db.UserBlocks.Add(block);
        await db.SaveChangesAsync();
        await AssertRedacted();

        db.UserBlocks.Remove(block);
        member.RemovedAt = now;
        await db.SaveChangesAsync();
        await AssertRedacted();

        async Task AssertRedacted()
        {
            var item = Assert.Single((await GetPage(recipient)).Items);
            Assert.Null(item.Destination);
            Assert.Equal("Este aviso ya no tiene detalles disponibles.", item.Message);
            Assert.Equal(1, await Unread(recipient));
        }
    }

    [Fact]
    public async Task TimelineLinksRespectCurrentEntryVisibility()
    {
        using var context = new GamesApiTestContext();
        using var recipient = await context.AuthenticatedClientAsync();
        using var actor = await context.AuthenticatedClientAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var game = GamePersistenceTestContext.LocalGame(now);
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var entry = new TimelineEntry
        {
            UserId = User(actor), GameId = game.Id, OccurredOn = DateOnly.FromDateTime(now),
            RecordedAt = now, Type = TimelineEntryType.Added,
        };
        db.TimelineEntries.Add(entry);
        await db.SaveChangesAsync();
        db.Notifications.Add(Row(User(recipient), now, User(actor), NotificationCenter.TimelineEntry, entry.Id));
        await db.SaveChangesAsync();

        Assert.Equal($"/entries/{entry.Id}", Assert.Single((await GetPage(recipient)).Items).Destination);
        entry.IsHidden = true;
        await db.SaveChangesAsync();
        Assert.Null(Assert.Single((await GetPage(recipient)).Items).Destination);
        using var link = await recipient.GetAsync($"/api/timeline/entries/{entry.Id}");
        Assert.Equal(HttpStatusCode.NotFound, link.StatusCode);
    }

    [Fact]
    public async Task OutboxRetriesDeliveryWithTheSameIdAndTheApiRecoversOfflineNotifications()
    {
        using var context = new GamesApiTestContext();
        using var recipient = await context.AuthenticatedClientAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var firstTab = await Open();
        using var secondTab = await Open();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new CatalogPersistenceClock { UtcNow = DateTimeOffset.UtcNow };
        var draft = new NotificationDraft(User(recipient), null, "probe", null, null);
        var publisher = scope.ServiceProvider.GetRequiredService<NotificationDelivery>();
        var handler = new TestProducer(publisher, draft);
        var outbox = new OutboxProcessor(db, [handler], clock, NullLogger<OutboxProcessor>.Instance);
        var first = await AddEvent();

        await outbox.ProcessBatchAsync(deadline.Token);
        var persisted = Assert.Single((await GetPage(recipient)).Items);
        Assert.Equal(persisted.Id, (await firstTab.EventAsync("NotificationAvailable"))[0].GetString());
        Assert.Equal(persisted.Id, (await secondTab.EventAsync("NotificationAvailable"))[0].GetString());
        Assert.Null((await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == first)).ProcessedAt);

        clock.UtcNow += TimeSpan.FromSeconds(2);
        await outbox.ProcessBatchAsync(deadline.Token);
        Assert.Equal(persisted.Id, (await firstTab.EventAsync("NotificationAvailable"))[0].GetString());
        Assert.Equal(persisted.Id, (await secondTab.EventAsync("NotificationAvailable"))[0].GetString());
        Assert.NotNull((await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == first)).ProcessedAt);
        Assert.Equal(1, await Unread(recipient));

        firstTab.Dispose();
        secondTab.Dispose();
        await AddEvent();
        await outbox.ProcessBatchAsync(deadline.Token);
        using var reconnected = await Open();
        Assert.Equal(2, (await GetPage(recipient)).Items.Count);
        Assert.Equal(2, await Unread(recipient));
        using var read = await recipient.PutAsync($"/api/notifications/{persisted.Id}/read", null, deadline.Token);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(0, (await reconnected.EventAsync("NotificationsRead")).GetArrayLength());
        Assert.Equal(1, await Unread(recipient));

        Task<ChatLiveTests.Socket> Open() => ChatLiveTests.Socket.OpenAsync(context, recipient, deadline.Token, NotificationHub.Path);
        async Task<Guid> AddEvent()
        {
            var item = new OutboxMessage
            {
                Id = Guid.NewGuid(), Type = handler.EventType, AggregateId = "1", Payload = "{}", OccurredAt = clock.UtcNow.UtcDateTime,
            };
            db.OutboxMessages.Add(item);
            await db.SaveChangesAsync(deadline.Token);
            return item.Id;
        }
    }

    private sealed class TestProducer(NotificationDelivery publisher, NotificationDraft draft) : IOutboxHandler
    {
        private bool failOnce = true;
        public string EventType => "TestNotificationDelivery";

        public async Task HandleAsync(OutboxMessage message, CancellationToken ct)
        {
            await publisher.PublishAsync(message.Id, draft, ct);
            if (!failOnce) return;
            failOnce = false;
            throw new InvalidOperationException("Simulate loss of the delivery acknowledgement.");
        }
    }

    private static Notification Row(int recipient, DateTime createdAt, int? actor = null, string? resourceType = null, long? resourceId = null)
        => new()
        {
            RecipientId = recipient, ActorId = actor, Type = "probe", ResourceType = resourceType, ResourceId = resourceId,
            CreatedAt = createdAt, EventId = Guid.NewGuid(), PayloadVersion = 1,
        };

    private static int User(HttpClient client)
        => int.Parse(new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject);

    private static async Task<NotificationPageDto> GetPage(HttpClient client, string query = "")
        => (await client.GetFromJsonAsync<NotificationPageDto>("/api/notifications" + query))!;

    private static async Task<int> Unread(HttpClient client)
        => (await client.GetFromJsonAsync<NotificationCountDto>("/api/notifications/unread-count"))!.UnreadCount;
}
