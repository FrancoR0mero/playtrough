using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Xunit;

namespace Playtrough.Tests;

public sealed class GroupManagementEndpointsTests
{
    [Fact]
    public async Task OnlyOwnerGetsVersionAndCanEditOrDelete()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var stranger = await context.AuthenticatedClientAsync();
        var group = await Create(owner);
        string version;
        using (var request = new HttpRequestMessage(HttpMethod.Get, group))
        {
            request.Headers.Add("Origin", "http://localhost:4200");
            using var response = await owner.SendAsync(request);
            response.EnsureSuccessStatusCode();
            version = response.Headers.ETag!.Tag;
            Assert.Contains("ETag", response.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(value => value.Split(',')));
        }
        using var strangerProfile = await stranger.GetAsync(group);
        Assert.Equal(HttpStatusCode.OK, strangerProfile.StatusCode);
        Assert.Null(strangerProfile.Headers.ETag);
        await Expect(Write(stranger, HttpMethod.Put, group, version), HttpStatusCode.NotFound);
        await Expect(Write(stranger, HttpMethod.Delete, group, version), HttpStatusCode.NotFound);
        await Expect(Write(owner, HttpMethod.Put, group, null), HttpStatusCode.PreconditionRequired);
        await Expect(Write(owner, HttpMethod.Delete, group, "\"invalid\""), HttpStatusCode.BadRequest);
        await Expect(Write(owner, HttpMethod.Put, group, version, new { name = " ", description = "", topic = "Inválido", privacy = "otra" }),
            HttpStatusCode.BadRequest);
        Assert.Equal(version, await Version(owner, group));

        await Expect(Write(owner, HttpMethod.Put, group, version), HttpStatusCode.NoContent);
        var next = await Version(owner, group);
        Assert.NotEqual(version, next);
        var profile = (await owner.GetFromJsonAsync<GroupProfileDto>(group))!;
        Assert.Equal(("Nombre actualizado", "Nueva descripción", "Indie", "private"),
            (profile.Name, profile.Description, profile.Topic, profile.Privacy));
        await Expect(Write(owner, HttpMethod.Put, group, version), HttpStatusCode.PreconditionFailed);
        await Expect(Write(owner, HttpMethod.Delete, group, version), HttpStatusCode.PreconditionFailed);
        Assert.Equal(next, await Version(owner, group));
    }

    [Fact]
    public async Task PrivacyChangesPreserveMembershipAndSoftDeletionClosesEveryGroupEndpoint()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var member = await context.AuthenticatedClientAsync();
        using var visitor = await context.AuthenticatedClientAsync();
        var group = await Create(owner);
        Assert.Equal("member", await Join(member, group));
        await Expect(Write(owner, HttpMethod.Put, group, await Version(owner, group)), HttpStatusCode.NoContent);
        Assert.Equal("member", (await member.GetFromJsonAsync<GroupProfileDto>(group))!.Membership);
        await Expect(member.GetAsync(group + "/members"), HttpStatusCode.OK);
        await Expect(member.GetAsync(group + "/reviews"), HttpStatusCode.OK);
        await Expect(visitor.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        await Expect(visitor.GetAsync(group + "/reviews"), HttpStatusCode.NotFound);
        Assert.Equal("pending", await Join(visitor, group));

        await Expect(Write(owner, HttpMethod.Put, group, await Version(owner, group),
            new { name = "Grupo abierto", description = "Descripción", topic = "Indie", privacy = "public" }), HttpStatusCode.NoContent);
        Assert.Equal("member", await Join(visitor, group));
        Assert.Empty((await owner.GetFromJsonAsync<GroupPeoplePageDto<GroupJoinRequestDto>>(group + "/join-requests"))!.Items);

        var version = await Version(owner, group);
        await Expect(Write(owner, HttpMethod.Delete, group, version), HttpStatusCode.NoContent);
        await Expect(Write(owner, HttpMethod.Delete, group, version), HttpStatusCode.NotFound);
        await Expect(Write(owner, HttpMethod.Put, group, version), HttpStatusCode.NotFound);
        await Expect(owner.GetAsync(group), HttpStatusCode.NotFound);
        await Expect(member.GetAsync(group), HttpStatusCode.NotFound);
        await Expect(member.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        await Expect(member.GetAsync(group + "/reviews"), HttpStatusCode.NotFound);
        await Expect(owner.GetAsync(group + "/join-requests"), HttpStatusCode.NotFound);
        await Expect(visitor.PutAsync(group + "/membership", null), HttpStatusCode.NotFound);
        Assert.Empty((await visitor.GetFromJsonAsync<GroupPageDto>("/api/groups"))!.Items);

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull((await db.SocialGroups.SingleAsync()).DeletedAt);
        Assert.Equal(3, await db.GroupMembers.CountAsync());
        Assert.Equal(1, await db.GroupJoinRequests.CountAsync());
    }

    [Fact]
    public async Task SimultaneousEditAndDeleteCannotBothUseTheSameVersion()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        var group = await Create(owner);
        var version = await Version(owner, group);
        var editTask = Write(owner, HttpMethod.Put, group, version);
        var deleteTask = Write(owner, HttpMethod.Delete, group, version);
        await Task.WhenAll(editTask, deleteTask);
        using var edit = await editTask;
        using var delete = await deleteTask;
        Assert.Single(new[] { edit, delete }, response => response.StatusCode == HttpStatusCode.NoContent);
        if (edit.StatusCode == HttpStatusCode.NoContent)
        {
            Assert.Equal(HttpStatusCode.PreconditionFailed, delete.StatusCode);
            Assert.Equal("Nombre actualizado", (await owner.GetFromJsonAsync<GroupProfileDto>(group))!.Name);
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
            await Expect(owner.GetAsync(group), HttpStatusCode.NotFound);
        }
    }

    private static async Task<string> Create(HttpClient owner)
    {
        using var response = await owner.PostAsJsonAsync("/api/groups",
            new { name = "Grupo", description = "Descripción", topic = "Retro", privacy = "public" });
        response.EnsureSuccessStatusCode();
        return response.Headers.Location!.AbsolutePath;
    }

    private static async Task<string> Version(HttpClient owner, string group)
    {
        using var response = await owner.GetAsync(group);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private static async Task<string> Join(HttpClient user, string group)
    {
        using var response = await user.PutAsync(group + "/membership", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupMembershipDto>())!.Status;
    }

    private static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string group, string? version, object? body = null)
    {
        using var request = new HttpRequestMessage(method, group);
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        if (method == HttpMethod.Put) request.Content = JsonContent.Create(body ??
            new { name = "Nombre actualizado", description = "Nueva descripción", topic = "Indie", privacy = "private" });
        return await client.SendAsync(request);
    }

    private static async Task Expect(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        Assert.Equal(status, response.StatusCode);
    }
}
