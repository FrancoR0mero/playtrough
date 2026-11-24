using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Events;
using Playtrough.Api.Hubs;
using Xunit;

namespace Playtrough.Tests;

public sealed class EssentialNotificationTests
{
    [Fact]
    public async Task SimulatedProducersSendAllEssentialNoticesOnlyToTheAffectedAccount()
    {
        using var context = new GamesApiTestContext();
        using var holder = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var user = UserId(holder);
        foreach (var category in new[] { "friends", "interactions", "groups" })
        {
            using var update = await holder.PutAsJsonAsync("/api/notifications/preferences",
                new { category, enabled = false }, deadline.Token);
            update.EnsureSuccessStatusCode();
        }

        using var socket = await ChatLiveTests.Socket.OpenAsync(context, holder, deadline.Token, NotificationHub.Path);
        using var otherSocket = await ChatLiveTests.Socket.OpenAsync(context, other, deadline.Token, NotificationHub.Path);
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        var now = DateTime.UtcNow;
        var decision = OutboxEvents.Enqueue(db, 10, new ModerationDecisionApplied(user), now);
        OutboxEvents.Enqueue(db, 11, new ModerationAppealResolved(user), now);
        OutboxEvents.Enqueue(db, 12, new SubscriptionActivated(user), now);
        OutboxEvents.Enqueue(db, 12, new PaymentFailed(user), now);
        OutboxEvents.Enqueue(db, 12, new SubscriptionExpired(user), now);
        await db.SaveChangesAsync(deadline.Token);
        await worker.ProcessBatchAsync(deadline.Token);

        var inbox = await Inbox(holder, deadline.Token);
        Assert.Equal(5, inbox.Count);
        Assert.All(inbox, notice => Assert.Null(notice.Destination));
        Assert.Equal(new[]
        {
            "No pudimos procesar el pago de tu suscripción.",
            "Se aplicó una decisión de moderación sobre tu contenido.",
            "Se resolvió tu apelación de moderación.",
            "Tu suscripción está activa.",
            "Tu suscripción venció.",
        }.Order(), inbox.Select(notice => notice.Message).Order());
        var delivered = new HashSet<string>();
        for (var index = 0; index < inbox.Count; index++)
            delivered.Add((await socket.EventAsync("NotificationAvailable"))[0].GetString()!);
        Assert.True(delivered.SetEquals(inbox.Select(item => item.Id)));

        foreach (var row in await db.OutboxMessages.AsNoTracking().ToListAsync(deadline.Token))
        {
            Assert.NotNull(row.ProcessedAt);
            using var payload = JsonDocument.Parse(row.Payload);
            var field = Assert.Single(payload.RootElement.EnumerateObject());
            Assert.Equal("recipientId", field.Name);
            Assert.Equal(user, field.Value.GetInt32());
        }
        Assert.False(await db.Notifications.AnyAsync(row => row.RecipientId != user || row.ActorId != null
            || row.ResourceId != null || row.ResourceType != null, deadline.Token));
        Assert.Empty(await Inbox(other, deadline.Token));
        var decisionId = await db.Notifications.Where(row => row.EventId == decision.Id).Select(row => row.Id).SingleAsync(deadline.Token);
        using var foreignRead = await other.PutAsync($"/api/notifications/{decisionId}/read", null, deadline.Token);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);

        await db.OutboxMessages.Where(row => row.Id == decision.Id).ExecuteUpdateAsync(
            update => update.SetProperty(row => row.ProcessedAt, (DateTime?)null), deadline.Token);
        await worker.ProcessBatchAsync(deadline.Token);
        Assert.Equal(decisionId.ToString(CultureInfo.InvariantCulture), (await socket.EventAsync("NotificationAvailable"))[0].GetString());
        Assert.Equal(5, (await Inbox(holder, deadline.Token)).Count);

        OutboxEvents.Enqueue(db, 20, new SubscriptionActivated(UserId(other)), now);
        await db.SaveChangesAsync(deadline.Token);
        await worker.ProcessBatchAsync(deadline.Token);
        var otherNotice = Assert.Single(await Inbox(other, deadline.Token));
        Assert.Equal(otherNotice.Id, (await otherSocket.EventAsync("NotificationAvailable"))[0].GetString());
    }

    [Fact]
    public async Task WorkerAcknowledgesAValidEventWhoseAccountNoLongerExists()
    {
        using var context = new GamesApiTestContext();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        var item = OutboxEvents.Enqueue(db, 1, new PaymentFailed(int.MaxValue), DateTime.UtcNow);
        await db.SaveChangesAsync();
        await worker.ProcessBatchAsync(default);

        Assert.False(await db.Notifications.AnyAsync());
        var processed = await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        Assert.NotNull(processed.ProcessedAt);
        Assert.Equal(1, processed.Attempts);
    }

    private static int UserId(HttpClient client) => int.Parse(new JwtSecurityTokenHandler()
        .ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject, CultureInfo.InvariantCulture);

    private static async Task<IReadOnlyList<NotificationItemDto>> Inbox(HttpClient client, CancellationToken ct)
        => (await client.GetFromJsonAsync<NotificationPageDto>("/api/notifications", ct))!.Items;
}
