using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Playtrough.Api.Authorization;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class AdminRoleTransactionTests
{
    [Fact]
    public async Task ChangeRole_WhenLastAdminWouldBeRemoved_Returns400AndPreservesAdmin()
    {
        // Arrange: a recently demoted admin still holds an access JWT until expiry.
        using var context = new AdminTestContext(); await context.InitializeAsync();
        var secondId = await context.CreateUserAsync("second_admin", "second_admin@example.com", AppRoles.Admin);
        using var first = await context.AdminClientAsync();
        using var second = context.Factory.CreateClient();
        var login = await context.LoginAsync(second, "second_admin");
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var demoteFirst = await second.PutAsJsonAsync($"/api/admin/users/{context.ActorId}/role", new { role = AppRoles.User });
        Assert.Equal(HttpStatusCode.OK, demoteFirst.StatusCode);

        // Act
        using var response = await first.PutAsJsonAsync($"/api/admin/users/{secondId}/role", new { role = AppRoles.User });

        // Assert
        var problem = await AdminUsersEndpointsTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("last_admin", problem.Extensions["code"]!.ToString());
        using var scope = context.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(secondId, Assert.Single(await users.GetUsersInRoleAsync(AppRoles.Admin)).Id);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RoleChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task ChangeRole_WithConcurrentCrossDemotions_LeavesOneAdminAndOneAudit()
    {
        // Arrange: separate connections to one temporary WAL database, as in refresh concurrency tests.
        using var context = new AdminTestContext(concurrent: true); await context.InitializeAsync();
        var secondId = await context.CreateUserAsync("second_admin", "second_admin@example.com", AppRoles.Admin);
        using var first = await context.AdminClientAsync();
        using var second = context.Factory.CreateClient();
        var login = await context.LoginAsync(second, "second_admin");
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage> SendAsync(HttpClient client, int id) => Task.Run(async () =>
        {
            await start.Task;
            return await client.PutAsJsonAsync($"/api/admin/users/{id}/role", new { role = AppRoles.User });
        });
        var firstTask = SendAsync(first, secondId);
        var secondTask = SendAsync(second, context.ActorId);

        // Act
        start.SetResult();
        var responses = await Task.WhenAll(firstTask, secondTask);
        using var firstResponse = responses[0]; using var secondResponse = responses[1];

        // Assert
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
        var problem = await AdminUsersEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.BadRequest);
        Assert.Equal("last_admin", problem.Extensions["code"]!.ToString());
        using var scope = context.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(await users.GetUsersInRoleAsync(AppRoles.Admin));
        Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RoleChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task ChangeRole_WhenRevocationFailsAfterWriting_RollsBackRoleAuditAndRevocation()
    {
        // Arrange
        using var context = new AdminTestContext(); await context.InitializeAsync();
        using var client = context.Factory.CreateClient();
        await context.LoginAsync(client, LoginApiFactory.Username);
        var original = await context.Factory.ReadUserAsync();
        using var scope = context.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var realRefresh = services.GetRequiredService<IRefreshTokenRepository>();
        var failingRefresh = new Mock<IRefreshTokenRepository>();
        failingRefresh.Setup(repository => repository.RevokeAllActiveAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(async (int id, DateTime now, CancellationToken ct) =>
            {
                await realRefresh.RevokeAllActiveAsync(id, now, ct);
                throw new InvalidOperationException("test failure after SQL update");
            });
        var service = new AdminUserService(services.GetRequiredService<IAdminUserRepository>(),
            services.GetRequiredService<IUserRepository>(), services.GetRequiredService<UserManager<ApplicationUser>>(),
            failingRefresh.Object, Mock.Of<ILogger<AdminUserService>>());

        // Act
        var result = await service.ChangeRoleAsync(context.ActorId, context.TargetId, AppRoles.Moderator);

        // Assert: read from a fresh scope, rather than the rolled-back tracked Identity entities.
        Assert.Equal(RoleChangeError.Unexpected, result.Error);
        using var readScope = context.Factory.Services.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = readScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(context.TargetId.ToString()))!;
        Assert.Equal(AppRoles.User, Assert.Single(await users.GetRolesAsync(user)));
        Assert.Equal(original.ConcurrencyStamp, user.ConcurrencyStamp);
        Assert.Empty(await db.RoleChangeLogs.ToListAsync());
        Assert.Null((await db.RefreshTokens.SingleAsync()).RevokedAt);
    }
}
