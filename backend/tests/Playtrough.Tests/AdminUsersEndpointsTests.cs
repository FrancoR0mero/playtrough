using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Authorization;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class AdminUsersEndpointsTests
{
    [Fact]
    public async Task ListUsers_WithPagination_ReturnsStablePagesAndOnlyPublicFields()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        using var first = await client.GetAsync("/api/admin/users?page=1&pageSize=2");
        var page1 = (await first.Content.ReadFromJsonAsync<PagedResponse<AdminUserDto>>())!;
        var page2 = (await client.GetFromJsonAsync<PagedResponse<AdminUserDto>>("/api/admin/users?page=2&pageSize=2"))!;
        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(3, page1.Total); Assert.Equal(1, page1.Page); Assert.Equal(2, page1.PageSize);
        Assert.Equal(2, page1.Items.Count); Assert.Single(page2.Items);
        Assert.Empty(page1.Items.Select(user => user.Id).Intersect(page2.Items.Select(user => user.Id)));
        Assert.Contains(page1.Items.Concat(page2.Items), user => user.Id == context.OtherId && !user.EmailConfirmed);
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            Assert.Equal(new[] { "email", "emailConfirmed", "id", "role", "username" }, item.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
    }

    [Theory]
    [InlineData("search=LOGIN_TEST", "login_test")]
    [InlineData("search=ADDRESS%40EXAMPLE.NET", "other_user")]
    [InlineData("role=Admin", "actor_admin")]
    [InlineData("search=actor&role=Admin", "actor_admin")]
    public async Task ListUsers_WithSearchOrRole_FiltersByPartialUsernameEmailAndRole(string query, string username)
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        var page = (await client.GetFromJsonAsync<PagedResponse<AdminUserDto>>($"/api/admin/users?{query}"))!;
        // Assert
        Assert.Equal(1, page.Total); Assert.Equal(username, Assert.Single(page.Items).Username);
    }

    [Theory]
    [InlineData("search=missing")]
    [InlineData("role=Moderator")]
    [InlineData("search=actor&role=User")]
    [InlineData("page=2147483647&pageSize=50")]
    public async Task ListUsers_WithNoResultsOrOutOfRangePage_ReturnsEmptyItems(string query)
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        var page = (await client.GetFromJsonAsync<PagedResponse<AdminUserDto>>($"/api/admin/users?{query}"))!;
        // Assert
        Assert.Empty(page.Items);
        Assert.Equal(query.StartsWith("page=") ? 3 : 0, page.Total);
    }

    [Theory]
    [InlineData("users?page=0")]
    [InlineData("users?pageSize=0")]
    [InlineData("users?pageSize=51")]
    [InlineData("users?role=Unknown")]
    [InlineData("role-changes?pageSize=51")]
    [InlineData("role-changes?targetUserId=0")]
    public async Task AdminLists_WithInvalidQuery_ReturnValidationProblem(string path)
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        using var response = await client.GetAsync($"/api/admin/{path}");
        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeRole_WithActiveSessions_AuditsRevokesAllTargetTokensAndRequiresFreshLogin()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var targetClient = context.Factory.CreateClient();
        var original = await context.LoginAsync(targetClient, LoginApiFactory.Username);
        await context.LoginAsync(targetClient, LoginApiFactory.Username);
        using var adminClient = await context.AdminClientAsync();
        var before = DateTime.UtcNow;
        // Act
        using var change = await adminClient.PutAsJsonAsync($"/api/admin/users/{context.TargetId}/role", new { role = AppRoles.Moderator });
        using var refresh = await targetClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        var nextLogin = await context.LoginAsync(targetClient, LoginApiFactory.Username);
        // Assert
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var updated = (await change.Content.ReadFromJsonAsync<AdminUserDto>())!;
        Assert.Equal(context.TargetId, updated.Id); Assert.Equal(AppRoles.Moderator, updated.Role);
        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized);
        Assert.Equal(AppRoles.User, Role(original.AccessToken)); Assert.Equal(AppRoles.Moderator, Role(nextLogin.AccessToken));
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(AppRoles.Moderator, Assert.Single(await users.GetRolesAsync((await users.FindByIdAsync(context.TargetId.ToString()))!)));
        var audit = await db.RoleChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(context.ActorId, audit.ChangedByUserId); Assert.Equal(context.TargetId, audit.TargetUserId);
        Assert.Equal(AppRoles.User, audit.OldRole); Assert.Equal(AppRoles.Moderator, audit.NewRole);
        Assert.InRange(audit.ChangedAt, before, DateTime.UtcNow);
        var revoked = await db.RefreshTokens.Where(token => token.UserId == context.TargetId && token.RevokedAt != null).ToListAsync();
        Assert.Equal(2, revoked.Count); Assert.All(revoked, token => Assert.Equal(audit.ChangedAt, token.RevokedAt));
        Assert.All(await db.RefreshTokens.Where(token => token.UserId == context.ActorId).ToListAsync(), token => Assert.Null(token.RevokedAt));
    }

    [Fact]
    public async Task ChangeRole_WithSameRoleTwice_DoesNotModifyUserRoleAuditOrTokens()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        var original = await context.Factory.ReadUserAsync();
        using var targetClient = context.Factory.CreateClient();
        await context.LoginAsync(targetClient, LoginApiFactory.Username);
        using var beforeScope = context.Factory.Services.CreateScope();
        var roleStamp = await beforeScope.ServiceProvider.GetRequiredService<AppDbContext>().Roles
            .Where(role => role.Name == AppRoles.Admin).Select(role => role.ConcurrencyStamp).SingleAsync();
        // Act
        using var first = await client.PutAsJsonAsync($"/api/admin/users/{context.TargetId}/role", new { role = AppRoles.User });
        using var second = await client.PutAsJsonAsync($"/api/admin/users/{context.TargetId}/role", new { role = AppRoles.User });
        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(original.ConcurrencyStamp, (await context.Factory.ReadUserAsync()).ConcurrencyStamp);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.RoleChangeLogs.ToListAsync());
        Assert.Equal(1, await db.UserRoles.CountAsync(link => link.UserId == context.TargetId));
        Assert.All(await db.RefreshTokens.ToListAsync(), token => Assert.Null(token.RevokedAt));
        Assert.Equal(roleStamp, await db.Roles.Where(role => role.Name == AppRoles.Admin).Select(role => role.ConcurrencyStamp).SingleAsync());
    }

    [Fact]
    public async Task ChangeRole_WithOwnAccount_Returns400WithStableCode()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        using var response = await client.PutAsJsonAsync($"/api/admin/users/{context.ActorId}/role", new { role = AppRoles.User });
        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("self_role_change", problem.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task ChangeRole_WithOwnUnchangedRole_IsIdempotentWithoutAuditOrRevocation()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        using var response = await client.PutAsJsonAsync($"/api/admin/users/{context.ActorId}/role", new { role = AppRoles.Admin });
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.RoleChangeLogs.ToListAsync());
        Assert.Null((await db.RefreshTokens.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task ChangeRole_WithTwoAdmins_DemotesOtherAdminAndPreservesOneAdmin()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        var otherAdmin = await context.CreateUserAsync("second_admin", "second_admin@example.com", AppRoles.Admin);
        using var client = await context.AdminClientAsync();
        // Act
        using var response = await client.PutAsJsonAsync($"/api/admin/users/{otherAdmin}/role", new { role = AppRoles.User });
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = context.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(await users.GetUsersInRoleAsync(AppRoles.Admin));
        Assert.Equal(AppRoles.User, Assert.Single(await users.GetRolesAsync((await users.FindByIdAsync(otherAdmin.ToString()))!)));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ChangeRole_WithInvalidRole_Returns400(string? role)
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        using var response = await client.PutAsJsonAsync($"/api/admin/users/{context.TargetId}/role", new { role });
        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeRole_WithMissingUser_Returns404()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        // Act
        using var response = await client.PutAsJsonAsync("/api/admin/users/2147483647/role", new { role = AppRoles.User });
        // Assert
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(AppRoles.User)]
    [InlineData(AppRoles.Moderator)]
    [InlineData(null)]
    public async Task AdminEndpoints_WithoutAdminRole_RejectAllActions(string? role)
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = context.Factory.CreateClient();
        if (role is not null)
        {
            var username = "restricted_test";
            await context.CreateUserAsync(username, "restricted_test@example.com", role);
            var login = await context.LoginAsync(client, username);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        }
        // Act
        using var users = await client.GetAsync("/api/admin/users");
        using var audit = await client.GetAsync("/api/admin/role-changes");
        using var update = await client.PutAsJsonAsync($"/api/admin/users/{context.TargetId}/role", new { role = AppRoles.Admin });
        // Assert
        foreach (var response in new[] { users, audit, update })
            await AssertProblemAsync(response, role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoleChanges_WithPaginationAndTargetFilter_ReturnNewestChangesFirst()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = await context.AdminClientAsync();
        foreach (var (id, role) in new[] { (context.OtherId, AppRoles.Moderator), (context.TargetId, AppRoles.Moderator), (context.TargetId, AppRoles.Admin) })
        {
            using var response = await client.PutAsJsonAsync($"/api/admin/users/{id}/role", new { role });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        // Act
        var first = (await client.GetFromJsonAsync<PagedResponse<RoleChangeDto>>("/api/admin/role-changes?pageSize=2"))!;
        var second = (await client.GetFromJsonAsync<PagedResponse<RoleChangeDto>>("/api/admin/role-changes?pageSize=2&page=2"))!;
        var filtered = (await client.GetFromJsonAsync<PagedResponse<RoleChangeDto>>($"/api/admin/role-changes?targetUserId={context.TargetId}"))!;
        // Assert
        Assert.Equal(3, first.Total); Assert.Equal(2, first.Items.Count); Assert.Single(second.Items);
        Assert.Equal(2, filtered.Total); Assert.All(filtered.Items, change => Assert.Equal(context.TargetId, change.TargetUserId));
        var all = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(all.OrderByDescending(change => change.ChangedAt).ThenByDescending(change => change.Id), all);
        Assert.Equal(new[] { AppRoles.Admin, AppRoles.Moderator }, filtered.Items.Select(change => change.NewRole));
        Assert.All(all, change => Assert.Equal(DateTimeKind.Utc, change.ChangedAt.Kind));
    }

    internal static async Task<ProblemDetails> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal((int)status, problem.Status);
        return problem;
    }

    private static string Role(string jwt) => Assert.Single(new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims, claim => claim.Type == "role").Value;
}
