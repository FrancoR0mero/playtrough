using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class SocialNotificationTests
{
    [Fact]
    public async Task FriendshipEventsReachOnlyTheOtherAccountAndHideDetailsAfterBlocking()
    {
        using var context = new GamesApiTestContext();
        using var sender = await context.AuthenticatedClientAsync();
        using var receiver = await context.AuthenticatedClientAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();

        using var sent = await sender.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        sent.EnsureSuccessStatusCode();
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        await Expect(HttpStatusCode.Conflict, sender.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" }));
        await worker.ProcessBatchAsync(default);
        Assert.Equal("/friends?tab=received", Assert.Single(await Inbox(receiver)).Destination);
        Assert.Empty(await Inbox(sender));

        var accept = $"/api/friends/requests/{request.Id}/accept";
        await Expect(HttpStatusCode.OK, receiver.PostAsync(accept, null));
        await Expect(HttpStatusCode.OK, receiver.PostAsync(accept, null));
        await worker.ProcessBatchAsync(default);
        Assert.Equal("/friends", Assert.Single(await Inbox(sender)).Destination);
        Assert.Null(Assert.Single(await Inbox(receiver)).Destination);

        await db.OutboxMessages.Where(item => item.Type == nameof(FriendRequestAccepted)).ExecuteUpdateAsync(
            update => update.SetProperty(item => item.ProcessedAt, (DateTime?)null));
        await worker.ProcessBatchAsync(default);
        Assert.Single(await Inbox(sender));
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
        await Expect(HttpStatusCode.NoContent, receiver.PostAsJsonAsync("/api/blocks", new { username = "games_user_1" }));
        var redacted = Assert.Single(await Inbox(sender));
        Assert.Null(redacted.Destination);
        Assert.Equal("Este aviso ya no tiene detalles disponibles.", redacted.Message);
    }

    [Fact]
    public async Task TimelineNotifiesRealLikeTransitionsAndCommentsWithoutLeakingCommentText()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var actor = await context.AuthenticatedClientAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        var entry = await SeedEntry(db, User(owner));
        var timeline = $"/api/timeline/entries/{entry.Id}";
        var reaction = timeline + "/reaction";
        var comments = timeline + "/comments";
        var link = $"/entries/{entry.Id}";
        var like = new { value = "like" };
        await Expect(HttpStatusCode.OK, actor.PutAsJsonAsync(reaction, like));
        await Expect(HttpStatusCode.OK, actor.PutAsJsonAsync(reaction, like));
        await Expect(HttpStatusCode.OK, actor.DeleteAsync(reaction));
        await Expect(HttpStatusCode.OK, actor.PutAsJsonAsync(reaction, like));
        await Expect(HttpStatusCode.Created, owner.PostAsJsonAsync(comments, new { text = "Comentario propio" }));
        await Expect(HttpStatusCode.Created, actor.PostAsJsonAsync(comments, new { text = "Contenido privado de prueba" }));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(item => item.Type == nameof(TimelineEntryLiked)));
        var comment = await db.OutboxMessages.SingleAsync(item => item.Type == nameof(TimelineEntryCommented));
        Assert.DoesNotContain("Contenido privado de prueba", comment.Payload);
        await worker.ProcessBatchAsync(default);
        var inbox = await Inbox(owner);
        Assert.Equal(3, inbox.Count);
        Assert.Equal(2, inbox.Count(item => item.Message == "A alguien le gustó tu publicación."));
        Assert.All(inbox, item => Assert.Equal(link, item.Destination));
        Assert.Empty(await Inbox(actor));

        var commentId = await db.TimelineEntryComments.Where(row => row.AuthorId == User(actor)).Select(row => row.Id).SingleAsync();
        await Expect(HttpStatusCode.OK, actor.DeleteAsync($"/api/timeline/comments/{commentId}"));
        Assert.Single(await Inbox(owner), item => item.Destination is null);
        await Expect(HttpStatusCode.OK, owner.PutAsJsonAsync("/api/notifications/preferences", new { category = "interactions", enabled = false }));
        await Expect(HttpStatusCode.Created, actor.PostAsJsonAsync(comments, new { text = "Suprimido por preferencia" }));
        await worker.ProcessBatchAsync(default);
        Assert.Equal(3, (await Inbox(owner)).Count);
        await Expect(HttpStatusCode.OK, owner.PutAsJsonAsync("/api/notifications/preferences", new { category = "interactions", enabled = true }));
        await Expect(HttpStatusCode.Created, actor.PostAsJsonAsync(comments, new { text = "Bloqueado antes de entregar" }));
        await Expect(HttpStatusCode.NoContent, owner.PostAsJsonAsync("/api/blocks", new { username = "games_user_2" }));
        await worker.ProcessBatchAsync(default);
        Assert.All(await Inbox(owner), item => Assert.Null(item.Destination));
        Assert.Equal(3, (await Inbox(owner)).Count);
        Assert.False(await db.OutboxMessages.AnyAsync(row => row.ProcessedAt == null));
    }

    [Fact]
    public async Task GroupRequestsAndDecisionsReachTheOwnerOrApplicantWithCurrentPermissions()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var applicant = await context.AuthenticatedClientAsync();
        using var rejected = await context.AuthenticatedClientAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        using var created = await owner.PostAsJsonAsync("/api/groups",
            new { name = "Grupo privado", description = "Descripción", genreId = "rpg",
                subgenreIds = new[] { "jrpg" }, privacy = "private" });
        created.EnsureSuccessStatusCode();
        var group = created.Headers.Location!.AbsolutePath;
        var link = group[4..];
        await Expect(HttpStatusCode.OK, applicant.PutAsync(group + "/membership", null));
        await worker.ProcessBatchAsync(default);
        Assert.Equal(link, Assert.Single(await Inbox(owner)).Destination);
        Assert.Empty(await Inbox(applicant));
        await Decide("approve");
        await worker.ProcessBatchAsync(default);
        Assert.Equal(link, Assert.Single(await Inbox(applicant)).Destination);

        await Expect(HttpStatusCode.NoContent, owner.DeleteAsync($"{group}/members/{User(applicant)}"));
        await worker.ProcessBatchAsync(default);
        var after = await Inbox(applicant);
        Assert.Equal(2, after.Count);
        Assert.All(after, item => Assert.Null(item.Destination));
        Assert.Single(after, item => item.Message == "Te removieron de un grupo.");

        await Expect(HttpStatusCode.OK, rejected.PutAsync(group + "/membership", null));
        await Decide("reject");
        await worker.ProcessBatchAsync(default);
        var rejection = Assert.Single(await Inbox(rejected));
        Assert.Equal("Rechazaron tu solicitud para unirte a un grupo.", rejection.Message);
        Assert.Null(rejection.Destination);
        Assert.Single(await Inbox(owner));

        async Task Decide(string decision)
        {
            var requests = (await owner.GetFromJsonAsync<GroupPeoplePageDto<GroupJoinRequestDto>>(group + "/join-requests"))!;
            await Expect(HttpStatusCode.NoContent,
                owner.PostAsJsonAsync($"{group}/join-requests/{Assert.Single(requests.Items).Id}/decision", new { decision }));
        }
    }

    [Fact]
    public async Task CommentAndCounterRollBackIfTheOutboxInsertFails()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var actor = await context.AuthenticatedClientAsync();
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await SeedEntry(db, User(owner));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_social_outbox BEFORE INSERT ON OutboxMessages
            BEGIN SELECT RAISE(ABORT, 'outbox unavailable'); END;
            """);
        await Expect(HttpStatusCode.InternalServerError,
            actor.PostAsJsonAsync($"/api/timeline/entries/{entry.Id}/comments", new { text = "Debe revertirse" }));
        Assert.False(await db.TimelineEntryComments.AnyAsync());
        Assert.Equal(0, await db.TimelineEntries.Select(row => row.CommentCount).SingleAsync());
        Assert.False(await db.OutboxMessages.AnyAsync());
    }

    private static async Task<TimelineEntry> SeedEntry(AppDbContext db, int owner)
    {
        var now = DateTime.UtcNow;
        var game = GamePersistenceTestContext.LocalGame(now);
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var entry = new TimelineEntry
        {
            UserId = owner, GameId = game.Id, OccurredOn = DateOnly.FromDateTime(now),
            RecordedAt = now, Type = TimelineEntryType.Added,
        };
        db.TimelineEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private static int User(HttpClient client) => int.Parse(new JwtSecurityTokenHandler()
        .ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject, CultureInfo.InvariantCulture);

    private static async Task<IReadOnlyList<NotificationItemDto>> Inbox(HttpClient client)
        => (await client.GetFromJsonAsync<NotificationPageDto>("/api/notifications"))!.Items;

    private static async Task Expect(HttpStatusCode status, Task<HttpResponseMessage> request)
    {
        using var response = await request;
        Assert.Equal(status, response.StatusCode);
    }
}
