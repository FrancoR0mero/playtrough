using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Migrations;
using Xunit;

namespace Playtrough.Tests;

public sealed class GroupClassificationEndpointsTests
{
    private const string Groups = "/api/groups";

    [Fact]
    public async Task CatalogHasIndependentTagsAndOneCanonicalEntryForArpg()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var catalog = (await owner.GetFromJsonAsync<GroupClassificationDto>($"{Groups}/classification"))!;

        Assert.Equal(22, catalog.Genres.Count);
        Assert.Equal(24, catalog.Tags.Count);
        Assert.All(catalog.Genres, genre => Assert.Contains(catalog.Subgenres, item => item.GenreId == genre.Id));
        Assert.Equal(catalog.Subgenres.Count, catalog.Subgenres.Select(item => item.Name).Distinct().Count());
        var actionRpg = Assert.Single(catalog.Subgenres, item => item.Alias == "ARPG");
        Assert.Equal(("rpg-de-accion", "RPG de acción", "rpg"), (actionRpg.Id, actionRpg.Name, actionRpg.GenreId));
        Assert.Empty(catalog.Tags.Select(item => item.Name)
            .Intersect(catalog.Genres.Select(item => item.Name).Concat(catalog.Subgenres.Select(item => item.Name))));
    }

    [Fact]
    public async Task DiscoveryCombinesClassificationFiltersBeforePaging()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var older = await Create(owner, ["jrpg", "rpg-por-turnos"], ["indie", "pc"]);
        var newer = await Create(owner, ["jrpg", "rpg-por-turnos"], ["indie", "pc"]);
        await Create(owner, ["jrpg"], ["indie", "pc"]);
        await Create(owner, ["jrpg", "rpg-por-turnos"], ["indie"]);
        var filters = $"{Groups}?q=club&genreId=rpg&subgenreIds=jrpg&subgenreIds=rpg-por-turnos&tagIds=indie&tagIds=pc&limit=1";

        var first = (await owner.GetFromJsonAsync<GroupPageDto>(filters))!;
        Assert.Equal(newer.Id, Assert.Single(first.Items).Id);
        Assert.Equal(newer.Id, first.NextCursor);
        var last = (await owner.GetFromJsonAsync<GroupPageDto>($"{filters}&cursor={first.NextCursor}"))!;
        Assert.Equal(older.Id, Assert.Single(last.Items).Id);
        Assert.Null(last.NextCursor);
    }

    [Fact]
    public async Task WritesRejectUnknownGenreWrongParentAndRepeatedSelections()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();

        await Invalid(new { genreId = "missing", subgenreIds = new[] { "jrpg" }, tagIds = new[] { "missing" } },
            "genreId", "subgenreIds", "tagIds");
        await Invalid(new { genreId = "rpg", subgenreIds = new[] { "fps" }, tagIds = new[] { "pc", " PC " } },
            "subgenreIds", "tagIds");
        await Invalid(new { genreId = "rpg", subgenreIds = new[] { "jrpg", "JRPG" }, tagIds = Array.Empty<string>() },
            "subgenreIds");
        await Invalid(new { genreId = "rpg", subgenreIds = Array.Empty<string>(), tagIds = Array.Empty<string>() },
            "subgenreIds");
        using var search = await owner.GetAsync($"{Groups}?genreId=rpg&subgenreIds=fps");
        Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);

        async Task Invalid(object classification, params string[] expectedErrors)
        {
            var fields = JsonSerializer.SerializeToElement(classification).EnumerateObject()
                .ToDictionary(item => item.Name, item => (object)item.Value);
            fields["name"] = "Club";
            fields["description"] = "Descripción";
            fields["privacy"] = "public";
            using var response = await owner.PostAsJsonAsync(Groups, fields);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expectedErrors, problem.GetProperty("errors").EnumerateObject()
                .Select(item => item.Name).OrderBy(item => item));
        }
    }

    [Fact]
    public async Task UpdatesReplaceClassificationAtomicallyWithGroupVersion()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var group = await Create(owner, ["jrpg"], ["pc"]);
        var url = $"{Groups}/{group.Id}";
        using var initial = await owner.GetAsync(url);
        var version = initial.Headers.ETag!.Tag;

        using var saved = await Update(version, "disparos", "fps", "indie");
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var stale = await Update(version, "rpg", "soulslike", "online");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var updated = await owner.GetAsync(url);
        var current = (await updated.Content.ReadFromJsonAsync<GroupProfileDto>())!;
        Assert.Equal("disparos", current.Genre!.Id);
        Assert.Equal("fps", Assert.Single(current.Subgenres).Id);
        Assert.Equal("indie", Assert.Single(current.Tags).Id);
        Assert.Equal("owner", current.Membership);

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_group_tag BEFORE INSERT ON GroupTagAssignments BEGIN SELECT RAISE(ABORT, 'test'); END;");
        using var failed = await Update(updated.Headers.ETag!.Tag, "rpg", "soulslike", "online");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        using var afterFailure = await owner.GetAsync(url);
        var unchanged = (await afterFailure.Content.ReadFromJsonAsync<GroupProfileDto>())!;
        Assert.Equal(updated.Headers.ETag, afterFailure.Headers.ETag);
        Assert.Equal("disparos", unchanged.Genre!.Id);
        Assert.Equal("fps", Assert.Single(unchanged.Subgenres).Id);
        Assert.Equal("indie", Assert.Single(unchanged.Tags).Id);

        async Task<HttpResponseMessage> Update(string etag, string genreId, string subgenreId, string tagId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = JsonContent.Create(new { name = "Club", description = "Descripción", privacy = "private",
                    genreId, subgenreIds = new[] { subgenreId }, tagIds = new[] { tagId } }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await owner.SendAsync(request);
        }
    }

    [Fact]
    public async Task BackfillReusesKnownTagsAndLeavesClassifiedGroupsUntouched()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var existing = await Create(owner, ["jrpg"], ["pc"]);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerId = await db.SocialGroups.Where(group => group.Id == existing.Id).Select(group => group.OwnerId).SingleAsync();
        foreach (var topic in new[] { " RETRO ", "General", "Personalizado" })
            db.SocialGroups.Add(new() { Name = topic, Topic = topic, Description = "Histórico", OwnerId = ownerId });
        await db.SaveChangesAsync();

        foreach (var sql in new BackfillGroupClassification().UpOperations.OfType<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(sql.Sql);

        var groups = (await owner.GetFromJsonAsync<GroupPageDto>(Groups))!.Items;
        var retro = Assert.Single(groups, group => group.Name == " RETRO ");
        Assert.Equal("otros", retro.Genre!.Id);
        Assert.Equal("retro", Assert.Single(retro.Tags).Id);
        Assert.Empty(retro.Subgenres);
        Assert.All(groups.Where(group => group.Name is "General" or "Personalizado"), group =>
        {
            Assert.Equal("otros", group.Genre!.Id);
            Assert.Empty(group.Tags);
        });
        var classified = Assert.Single(groups, group => group.Id == existing.Id);
        Assert.Equal("rpg", classified.Genre!.Id);
        Assert.Equal("pc", Assert.Single(classified.Tags).Id);
    }

    private static async Task<GroupDto> Create(HttpClient client, string[] subgenreIds, string[] tagIds)
    {
        using var response = await client.PostAsJsonAsync(Groups,
            new { name = "Club de RPG", description = "Descripción", privacy = "public", genreId = "rpg",
                subgenreIds, tagIds });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>())!;
    }
}
