using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Xunit;

namespace Playtrough.Tests;

public sealed class GroupsEndpointsTests
{
    private const string Groups = "/api/groups";

    [Fact]
    public async Task CreateAndDiscoverUseAuthenticatedUsersAndMinimalGroupData()
    {
        using var context = new GamesApiTestContext();
        using var anonymous = context.Factory.CreateClient();
        using var unauthenticatedList = await anonymous.GetAsync(Groups);
        using var unauthenticatedCreate = await anonymous.PostAsJsonAsync(Groups, Draft("Grupo"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedCreate.StatusCode);

        using var owner = await context.AuthenticatedClientAsync();
        using var visitor = await context.AuthenticatedClientAsync();
        using var response = await owner.PostAsJsonAsync(Groups,
            new { name = "  Equipo 🎮  ", description = "  Juegos de antes  ", genreId = "RPG",
                subgenreIds = new[] { "jrpg" }, tagIds = new[] { "Retro" }, privacy = "private", ownerId = int.MaxValue });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<GroupDto>())!;
        Assert.Equal(("Equipo 🎮", "Juegos de antes", "rpg", "private"),
            (created.Name, created.Description, created.Genre.Id, created.Privacy));
        Assert.Equal("retro", Assert.Single(created.Tags).Id);
        Assert.Equal($"{Groups}/{created.Id}", response.Headers.Location!.AbsolutePath);

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerId = await db.Users.Where(user => user.UserName == "games_user_1").Select(user => user.Id).SingleAsync();
        var stored = await db.SocialGroups.SingleAsync();
        var membership = await db.GroupMembers.SingleAsync();
        Assert.Equal(ownerId, stored.OwnerId);
        Assert.Equal((created.Id, ownerId, stored.CreatedAt), (membership.GroupId, membership.UserId, membership.JoinedAt));
        Assert.NotEqual(Guid.Empty, stored.ConcurrencyToken);

        var page = (await visitor.GetFromJsonAsync<GroupPageDto>(Groups))!;
        var listed = Assert.Single(page.Items);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(created.Genre, listed.Genre);
        Assert.Equal(created.Tags, listed.Tags);
        var detail = await visitor.GetFromJsonAsync<JsonElement>($"{Groups}/{created.Id}");
        Assert.Equal(new[] { "description", "genre", "id", "membership", "name", "privacy", "subgenres", "tags" },
            detail.EnumerateObject().Select(field => field.Name).OrderBy(name => name).ToArray());
        Assert.Equal("none", detail.GetProperty("membership").GetString());
    }

    [Fact]
    public async Task ValidationRejectsInvalidFieldsAndFilters()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var response = await client.PostAsJsonAsync(Groups,
            new { name = " ", description = new string('x', 501), genreId = "invalido",
                subgenreIds = new[] { "jrpg" }, privacy = "friends" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "description", "genreId", "name", "privacy", "subgenreIds" },
            problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).OrderBy(name => name).ToArray());
        foreach (var query in new[] { "limit=51", "cursor=0", "genreId=invalido", "q=" + new string('x', 101) })
        {
            using var invalid = await client.GetAsync($"{Groups}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Empty((await client.GetFromJsonAsync<GroupPageDto>(Groups))!.Items);
    }

    [Fact]
    public async Task DiscoveryPagesEqualNamesAndFiltersLiteralTextTagsAndDeletedGroups()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var older = await Create(client, "100% Retro");
        var newer = await Create(client, "100% Retro");
        var deleted = await Create(client, "100% Retro");
        await Create(client, "1000 Retro");
        await Create(client, "100% Retro", "indie");
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SocialGroups.Where(group => group.Id == deleted.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(group => group.DeletedAt, DateTime.UtcNow));

        var url = $"{Groups}?q=100%25%20retro&tagIds=retro&limit=1";
        var first = (await client.GetFromJsonAsync<GroupPageDto>(url))!;
        Assert.Equal(newer.Id, Assert.Single(first.Items).Id);
        Assert.Equal(newer.Id, first.NextCursor);
        await Create(client, "100% Retro");
        var last = (await client.GetFromJsonAsync<GroupPageDto>($"{url}&cursor={first.NextCursor}"))!;
        Assert.Equal(older.Id, Assert.Single(last.Items).Id);
        Assert.Null(last.NextCursor);
        using var missing = await client.GetAsync($"{Groups}/{deleted.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task FailedOwnerMembershipRollsBackGroupCreation()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_membership BEFORE INSERT ON GroupMembers BEGIN SELECT RAISE(ABORT, 'test'); END;");

        using var response = await client.PostAsJsonAsync(Groups, Draft("Sin dueño"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(await db.SocialGroups.AnyAsync());
        Assert.False(await db.GroupMembers.AnyAsync());
    }

    private static object Draft(string name, string tag = "retro")
        => new { name, description = "Descripción del grupo", genreId = "rpg",
            subgenreIds = new[] { "jrpg" }, tagIds = new[] { tag }, privacy = "public" };

    private static async Task<GroupDto> Create(HttpClient client, string name, string tag = "retro")
    {
        using var response = await client.PostAsJsonAsync(Groups, Draft(name, tag));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!;
    }
}
