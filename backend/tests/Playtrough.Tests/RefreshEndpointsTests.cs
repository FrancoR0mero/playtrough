using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class RefreshEndpointsTests
{
    private const string InvalidDetail = "The refresh token is invalid or cannot be used.";

    [Fact]
    public async Task Refresh_AfterLogin_ReturnsRotatedTokensAcceptedByProtectedEndpoint()
    {
        // Arrange: the refresh request deliberately has no Authorization header.
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var login = await LoginAsync(client);

        // Act
        using var response = await RefreshAsync(client, login.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await response.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(refreshed);
        Assert.NotEqual(login.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(login.User, refreshed.User);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        using var protectedResponse = await client.GetAsync("/__tests/protected");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        var stored = await factory.ReadRefreshTokensAsync();
        Assert.Equal(2, stored.Count);
        var original = Assert.Single(stored, token => token.ReplacedByToken is not null);
        Assert.NotNull(original.RevokedAt);
        Assert.True(Assert.Single(stored, token => token.TokenHash == original.ReplacedByToken).IsActive(DateTime.UtcNow));
    }

    [Fact]
    public async Task Refresh_WithUsedToken_Returns401AndInvalidatesReplacement()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var login = await LoginAsync(client);
        using var first = await RefreshAsync(client, login.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replacement = await first.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(replacement);

        // Act
        using var reused = await RefreshAsync(client, login.RefreshToken);
        using var revokedReplacement = await RefreshAsync(client, replacement.RefreshToken);

        // Assert
        await AssertInvalidAsync(reused);
        await AssertInvalidAsync(revokedReplacement);
        Assert.All(await factory.ReadRefreshTokensAsync(), token => Assert.NotNull(token.RevokedAt));
    }

    [Theory]
    [InlineData("empty-body")]
    [InlineData("null-json")]
    [InlineData("missing-property")]
    [InlineData("empty-token")]
    [InlineData("whitespace")]
    public async Task Refresh_WithoutRequiredToken_Returns400(string invalid)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();

        // Act
        using var response = invalid switch
        {
            "empty-body" => await client.PostAsync("/api/auth/refresh", null),
            "null-json" => await client.PostAsJsonAsync<object?>("/api/auth/refresh", null),
            "missing-property" => await client.PostAsJsonAsync("/api/auth/refresh", new { }),
            "empty-token" => await RefreshAsync(client, ""),
            _ => await RefreshAsync(client, "   "),
        };

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
        Assert.Empty(await factory.ReadRefreshTokensAsync());
    }

    [Theory]
    [InlineData("nonexistent")]
    [InlineData("expired")]
    [InlineData("logout")]
    [InlineData("locked")]
    [InlineData("deleted-user")]
    public async Task Refresh_WithInvalidTokenOrUser_ReturnsSame401Problem(string invalid)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var login = await LoginAsync(client);
        var token = login.RefreshToken;
        if (invalid == "nonexistent") token = "nonexistent_refresh_token";
        else if (invalid == "logout")
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            using var logout = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequestDto { RefreshToken = token });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (invalid == "expired")
                await db.RefreshTokens.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            else
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByNameAsync(LoginApiFactory.Username);
                Assert.NotNull(user);
                var changed = invalid == "locked"
                    ? await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15))
                    : await users.DeleteAsync(user);
                Assert.True(changed.Succeeded);
            }
        }

        // Act
        using var response = await RefreshAsync(client, token);

        // Assert
        await AssertInvalidAsync(response);
    }

    [Fact]
    public async Task Refresh_WithRoleChangedInDatabase_UsesCurrentRole()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var login = await LoginAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(LoginApiFactory.Username);
            Assert.NotNull(user);
            Assert.True((await users.RemoveFromRoleAsync(user, "User")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        }

        // Act
        using var response = await RefreshAsync(client, login.RefreshToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await response.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(refreshed);
        Assert.Equal("Admin", refreshed.User.Role);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken);
        Assert.Equal("Admin", Assert.Single(jwt.Claims, claim => claim.Type == "role").Value);
    }

    [Fact]
    public async Task Refresh_WithConcurrentRequests_OnlyOneSucceedsAndReplayRevokesReplacement()
    {
        // Arrange: use distinct database connections, not a shared in-memory connection.
        using var factory = new LoginApiFactory(useIndependentConnections: true);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await factory.CreateUserAsync();
        var login = await LoginAsync(firstClient);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage> SendAsync(HttpClient client) => Task.Run(async () =>
        {
            await start.Task;
            return await RefreshAsync(client, login.RefreshToken);
        });
        var firstTask = SendAsync(firstClient);
        var secondTask = SendAsync(secondClient);

        // Act
        start.SetResult();
        var responses = await Task.WhenAll(firstTask, secondTask);
        using var first = responses[0];
        using var second = responses[1];

        // Assert
        var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        await AssertInvalidAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized));
        var replacement = await success.Content.ReadFromJsonAsync<RefreshResponseDto>();
        Assert.NotNull(replacement);
        var stored = await factory.ReadRefreshTokensAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, token => Assert.NotNull(token.RevokedAt));
        using var invalidated = await RefreshAsync(firstClient, replacement.RefreshToken);
        await AssertInvalidAsync(invalidated);
    }

    private static async Task<LoginResponseDto> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Identifier = LoginApiFactory.Username, Password = LoginApiFactory.Password,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        return result;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string token)
        => client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDto { RefreshToken = token });

    private static async Task AssertInvalidAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(401, problem.Status);
        Assert.Equal("Invalid refresh token", problem.Title);
        Assert.Equal(InvalidDetail, problem.Detail);
    }
}
