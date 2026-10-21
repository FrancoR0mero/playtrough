using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Authorization;
using Xunit;

namespace Playtrough.Tests;

public class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(AppRoles.User, AppPolicies.RequireUser, true)]
    [InlineData(AppRoles.User, AppPolicies.RequireModerator, false)]
    [InlineData(AppRoles.User, AppPolicies.RequireAdmin, false)]
    [InlineData(AppRoles.Moderator, AppPolicies.RequireUser, true)]
    [InlineData(AppRoles.Moderator, AppPolicies.RequireModerator, true)]
    [InlineData(AppRoles.Moderator, AppPolicies.RequireAdmin, false)]
    [InlineData(AppRoles.Admin, AppPolicies.RequireUser, true)]
    [InlineData(AppRoles.Admin, AppPolicies.RequireModerator, true)]
    [InlineData(AppRoles.Admin, AppPolicies.RequireAdmin, true)]
    [InlineData("Unknown", AppPolicies.RequireUser, false)]
    public async Task Policy_WithAuthenticatedRole_RespectsHierarchy(string role, string policy, bool allowed)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", role)], "Bearer", "name", "role"));

        // Act
        var result = await authorization.AuthorizeAsync(principal, null, policy);

        // Assert
        Assert.Equal(allowed, result.Succeeded);
    }

    [Theory]
    [InlineData(AppPolicies.RequireUser)]
    [InlineData(AppPolicies.RequireModerator)]
    [InlineData(AppPolicies.RequireAdmin)]
    public async Task Policy_WithUnauthenticatedPrincipal_RejectsEvenAdminClaim(string policy)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", AppRoles.Admin)], null, "name", "role"));

        // Act
        var result = await authorization.AuthorizeAsync(principal, null, policy);

        // Assert
        Assert.False(result.Succeeded);
    }
}
