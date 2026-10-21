using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Authorization;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class RoleClaimsEndpointsTests
{
    [Theory]
    [InlineData(AppRoles.User, AppRoles.Moderator)]
    [InlineData(AppRoles.Moderator, AppRoles.Admin)]
    [InlineData(AppRoles.Admin, AppRoles.User)]
    public async Task LoginAndRefresh_AfterRoleChangesInDatabase_EmitOneCurrentRole(string initial, string updated)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        await SetRoleAsync(factory, initial);

        // Act
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", EmailConfirmationTestContext.Login());
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        await SetRoleAsync(factory, updated);
        // Direct data change tests refresh's source of truth. The admin endpoint in phase 3
        // will separately revoke refresh tokens when it changes a role.
        using var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.RefreshToken });

        // Assert
        Assert.Equal(initial, Role(login.AccessToken));
        Assert.Equal(initial, login.User.Role);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refresh = (await refreshResponse.Content.ReadFromJsonAsync<RefreshResponseDto>())!;
        Assert.Equal(updated, Role(refresh.AccessToken));
        Assert.Equal(updated, refresh.User.Role);
    }

    [Fact]
    public async Task Register_WithValidRequest_AssignsOnlyUserRole()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(LoginApiFactory.Email))!;
        Assert.Equal(AppRoles.User, Assert.Single(await users.GetRolesAsync(user)));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.ToListAsync());
    }

    private static string Role(string token) => Assert.Single(new JwtSecurityTokenHandler()
        .ReadJwtToken(token).Claims, claim => claim.Type == "role").Value;

    private static async Task SetRoleAsync(LoginApiFactory factory, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(LoginApiFactory.Email))!;
        Assert.True((await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
    }
}
