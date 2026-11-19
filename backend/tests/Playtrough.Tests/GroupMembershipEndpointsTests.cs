using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class GroupMembershipEndpointsTests
{
    [Fact]
    public async Task PublicMembershipIsIdempotentAndOwnerCanExpelButMustRemain()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var visitor = await context.AuthenticatedClientAsync();
        var group = await Create(owner, "public");

        Assert.Equal("owner", await Status(owner, group));
        Assert.Equal("none", await Status(visitor, group));
        await Expect(visitor.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        await Expect(owner.DeleteAsync(group + "/membership"), HttpStatusCode.Conflict);
        var joins = await Task.WhenAll(Join(visitor, group), Join(visitor, group));
        Assert.All(joins, status => Assert.Equal("member", status));

        var page = (await visitor.GetFromJsonAsync<GroupPeoplePageDto<GroupMemberDto>>(group + "/members?limit=1"))!;
        var first = Assert.Single(page.Items);
        Assert.True(first.IsOwner);
        Assert.Equal(first.UserId, page.NextCursor);
        var last = (await visitor.GetFromJsonAsync<GroupPeoplePageDto<GroupMemberDto>>(
            $"{group}/members?limit=1&cursor={page.NextCursor}"))!;
        var member = Assert.Single(last.Items);
        Assert.False(member.IsOwner);
        Assert.Null(last.NextCursor);
        await Expect(visitor.DeleteAsync($"{group}/members/{first.UserId}"), HttpStatusCode.NotFound);
        await Expect(owner.DeleteAsync($"{group}/members/{first.UserId}"), HttpStatusCode.Conflict);

        await Expect(owner.DeleteAsync($"{group}/members/{member.UserId}"), HttpStatusCode.NoContent);
        await Expect(owner.DeleteAsync($"{group}/members/{member.UserId}"), HttpStatusCode.NoContent);
        Assert.Equal("removed", await Status(visitor, group));
        await Expect(visitor.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        Assert.Equal("member", await Join(visitor, group));
        await Expect(visitor.DeleteAsync(group + "/membership"), HttpStatusCode.NoContent);
        await Expect(visitor.DeleteAsync(group + "/membership"), HttpStatusCode.NoContent);
        Assert.Equal("none", await Status(visitor, group));

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.GroupMembers.ToListAsync());
        Assert.Equal(nameof(GroupMemberRemoved), (await db.OutboxMessages.SingleAsync()).Type);
    }

    [Fact]
    public async Task PrivateRequestsAreScopedAndDecisionsAreIdempotent()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var applicant = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        var group = await Create(owner, "private");
        var otherGroup = await Create(owner, "private");

        Assert.All(await Task.WhenAll(Join(applicant, group), Join(applicant, group)), status => Assert.Equal("pending", status));
        Assert.Equal("none", await Status(other, group));
        await Expect(applicant.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        await Expect(applicant.GetAsync(group + "/join-requests"), HttpStatusCode.NotFound);
        var request = await Request(owner, group);
        Assert.Equal("games_user_2", request.Username);
        await Expect(Decide(other, group, request.Id, "approve"), HttpStatusCode.NotFound);
        await Expect(Decide(owner, otherGroup, request.Id, "approve"), HttpStatusCode.NotFound);
        await Expect(Decide(owner, group, request.Id, "maybe"), HttpStatusCode.BadRequest);
        await Expect(Decide(owner, group, request.Id, "reject"), HttpStatusCode.NoContent);
        Assert.Equal("rejected", await Status(applicant, group));
        Assert.Equal("pending", await Join(applicant, group));
        var retry = await Request(owner, group);
        Assert.NotEqual(request.Id, retry.Id);

        await Task.WhenAll(
            Expect(Decide(owner, group, retry.Id, "approve"), HttpStatusCode.NoContent),
            Expect(Decide(owner, group, retry.Id, "approve"), HttpStatusCode.NoContent));
        await Expect(Decide(owner, group, retry.Id, "reject"), HttpStatusCode.Conflict);
        Assert.Equal("member", await Status(applicant, group));
        var members = (await applicant.GetFromJsonAsync<GroupPeoplePageDto<GroupMemberDto>>(group + "/members"))!;
        Assert.Equal(2, members.Items.Count);
        await Expect(applicant.DeleteAsync(group + "/membership"), HttpStatusCode.NoContent);
        await Expect(Decide(owner, group, retry.Id, "approve"), HttpStatusCode.NoContent);
        await Expect(applicant.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        Assert.Equal("none", await Status(applicant, group));

        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = await db.OutboxMessages.ToListAsync();
        Assert.Equal(2, events.Count(item => item.Type == nameof(GroupJoinRequested)));
        Assert.Equal(2, events.Count(item => item.Type == nameof(GroupJoinResolved)));
        Assert.Single(events, item => item.Type == nameof(GroupJoinResolved) &&
            JsonSerializer.Deserialize<GroupJoinResolved>(item.Payload, JsonSerializerOptions.Web) is { Approved: true });
    }

    [Fact]
    public async Task RacingApprovalAndLeaveEndWithoutAnActiveMembership()
    {
        using var context = new GamesApiTestContext(concurrent: true);
        using var owner = await context.AuthenticatedClientAsync();
        using var applicant = await context.AuthenticatedClientAsync();
        var group = await Create(owner, "private");
        Assert.Equal("pending", await Join(applicant, group));
        var request = await Request(owner, group);

        var approval = Decide(owner, group, request.Id, "approve");
        var exit = applicant.DeleteAsync(group + "/membership");
        await Task.WhenAll(approval, exit);
        using var approvalResponse = await approval;
        using var exitResponse = await exit;
        Assert.Contains(approvalResponse.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict });
        Assert.Equal(HttpStatusCode.NoContent, exitResponse.StatusCode);
        Assert.Equal("none", await Status(applicant, group));
        await Expect(applicant.GetAsync(group + "/members"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PrivacyChangePreservesMembersAndDeletedGroupTurnsInaccessible()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var member = await context.AuthenticatedClientAsync();
        using var visitor = await context.AuthenticatedClientAsync();
        var group = await Create(owner, "public");
        Assert.Equal("member", await Join(member, group));
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SocialGroups.ExecuteUpdateAsync(update => update.SetProperty(item => item.Privacy, GroupPrivacy.Private));
        await Expect(member.GetAsync(group + "/members"), HttpStatusCode.OK);
        await Expect(visitor.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        Assert.Equal("pending", await Join(visitor, group));
        await Expect(visitor.DeleteAsync(group + "/membership"), HttpStatusCode.NoContent);
        Assert.Equal("none", await Status(visitor, group));
        await db.SocialGroups.ExecuteUpdateAsync(update => update.SetProperty(item => item.DeletedAt, DateTime.UtcNow));
        await Expect(owner.GetAsync(group), HttpStatusCode.NotFound);
        await Expect(member.GetAsync(group + "/members"), HttpStatusCode.NotFound);
        await Expect(visitor.PutAsync(group + "/membership", null), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OutboxFailureRollsBackSavedRequest()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var applicant = await context.AuthenticatedClientAsync();
        var group = await Create(owner, "private");
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_outbox BEFORE INSERT ON OutboxMessages BEGIN SELECT RAISE(ABORT, 'test'); END;");
        await Expect(applicant.PutAsync(group + "/membership", null), HttpStatusCode.InternalServerError);
        Assert.False(await db.GroupJoinRequests.AnyAsync());
        Assert.False(await db.OutboxMessages.AnyAsync());
        Assert.Equal("none", await Status(applicant, group));
    }

    private static async Task<string> Create(HttpClient client, string privacy)
    {
        using var response = await client.PostAsJsonAsync("/api/groups",
            new { name = "Grupo de prueba", description = "Descripción", topic = "Retro", privacy });
        response.EnsureSuccessStatusCode();
        return response.Headers.Location!.AbsolutePath;
    }

    private static async Task<string> Join(HttpClient client, string group)
    {
        using var response = await client.PutAsync(group + "/membership", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupMembershipDto>())!.Status;
    }

    private static async Task<string> Status(HttpClient client, string group)
        => (await client.GetFromJsonAsync<GroupProfileDto>(group))!.Membership;

    private static async Task<GroupJoinRequestDto> Request(HttpClient owner, string group)
        => Assert.Single((await owner.GetFromJsonAsync<GroupPeoplePageDto<GroupJoinRequestDto>>(group + "/join-requests"))!.Items);

    private static Task<HttpResponseMessage> Decide(HttpClient client, string group, int requestId, string decision)
        => client.PostAsJsonAsync($"{group}/join-requests/{requestId}/decision", new { decision });

    private static async Task Expect(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        Assert.Equal(status, response.StatusCode);
    }
}
