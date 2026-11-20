using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Hubs;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public sealed class NotificationPreferencesTests
{
    private const string Path = "/api/notifications/preferences";

    [Fact]
    public async Task WritesOnlyChangeTheAuthenticatedAccountAndRemainUniquePerCategory()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        using var anonymous = context.Factory.CreateClient();
        using var noList = await anonymous.GetAsync(Path);
        using var noUpdate = await anonymous.PutAsJsonAsync(Path, new { category = "friends", enabled = false });
        Assert.Equal(HttpStatusCode.Unauthorized, noList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noUpdate.StatusCode);

        var defaults = await List(owner);
        Assert.Equal(new[] { "friends", "interactions", "groups", "account", "moderation" }, defaults.Select(item => item.Category));
        Assert.All(defaults, item => Assert.True(item.Enabled));
        Assert.Equal(new[] { "account", "moderation" }, defaults.Where(item => !item.Editable).Select(item => item.Category));

        var writes = await Task.WhenAll(Set(owner, "friends", false), Set(owner, "friends", false));
        Assert.All(writes, item => Assert.Equal(new NotificationPreferenceDto("friends", false, true), item));
        Assert.False((await List(owner)).Single(item => item.Category == "friends").Enabled);
        Assert.All(await List(other), item => Assert.True(item.Enabled));

        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.NotificationPreferences.AsNoTracking().SingleAsync();
        Assert.Equal(User(owner), persisted.UserId);
        Assert.Equal("friends", persisted.Category);
        Assert.True((await Set(owner, "friends", true)).Enabled);
        Assert.True((await db.NotificationPreferences.AsNoTracking().SingleAsync()).Enabled);
    }

    [Fact]
    public async Task InvalidCategoriesAndCrossAccountPayloadsAreRejectedBeforeWriting()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        object[] invalid =
        [
            new { category = "unknown", enabled = false },
            new { category = "FRIENDS", enabled = false },
            new { category = "account", enabled = false },
            new { category = "moderation", enabled = false },
            new { category = "friends" },
            new { enabled = false },
            new { category = "friends", enabled = "false" },
            new { category = "friends", enabled = false, userId = User(other) },
        ];
        foreach (var body in invalid)
        {
            using var response = await owner.PutAsJsonAsync(Path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.NotificationPreferences.AnyAsync());
    }

    [Fact]
    public async Task DeliveryHonorsChangesForNewSocialNoticesAndKeepsEssentialNoticesEnabled()
    {
        using var context = new GamesApiTestContext();
        using var recipient = await context.AuthenticatedClientAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var connection = await ChatLiveTests.Socket.OpenAsync(context, recipient, deadline.Token, NotificationHub.Path);
        await using var scope = context.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = scope.ServiceProvider.GetRequiredService<NotificationDelivery>();
        var user = User(recipient);

        await Publish(NotificationCategory.Interactions, "before");
        await AssertNext("before");

        await Set(recipient, "friends", false);
        await Set(recipient, "groups", false);
        await Set(recipient, "interactions", false);
        await Publish(NotificationCategory.Interactions, "disabled");
        // Even a stale persisted override cannot disable an essential category.
        db.NotificationPreferences.Add(new NotificationPreference { UserId = user, Category = "moderation", Enabled = false });
        await db.SaveChangesAsync(deadline.Token);
        Assert.True((await List(recipient)).Single(item => item.Category == "moderation").Enabled);
        await Publish(NotificationCategory.Moderation, "decision");
        await AssertNext("decision");
        await Publish(NotificationCategory.Account, "subscription");
        await AssertNext("subscription");

        await Set(recipient, "interactions", true);
        await Publish(NotificationCategory.Interactions, "reactivated");
        await AssertNext("reactivated");
        Assert.Equal(4, await db.Notifications.CountAsync());

        Task Publish(NotificationCategory category, string type) => delivery.PublishAsync(Guid.NewGuid(),
            category, new NotificationDraft(user, null, type, null, null), deadline.Token);

        async Task AssertNext(string type)
        {
            var id = await db.Notifications.AsNoTracking().Where(item => item.Type == type).Select(item => item.Id).SingleAsync(deadline.Token);
            Assert.Equal(id.ToString(CultureInfo.InvariantCulture), (await connection.EventAsync("NotificationAvailable"))[0].GetString());
        }
    }

    private static int User(HttpClient client) => int.Parse(
        new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject,
        CultureInfo.InvariantCulture);

    private static async Task<NotificationPreferenceDto[]> List(HttpClient client)
        => (await client.GetFromJsonAsync<NotificationPreferenceDto[]>(Path))!;

    private static async Task<NotificationPreferenceDto> Set(HttpClient client, string category, bool enabled)
    {
        using var response = await client.PutAsJsonAsync(Path, new { category, enabled });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NotificationPreferenceDto>())!;
    }
}
