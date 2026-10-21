using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Playtrough.Api.Authorization;
using Playtrough.Api.Models;
using Playtrough.Api.Dtos.Auth;
using Xunit;

namespace Playtrough.Tests;

public class RoleAuthorizationEndpointsTests
{
    public static TheoryData<string, string, HttpStatusCode> AccessMatrix
    {
        get
        {
            var cases = new TheoryData<string, string, HttpStatusCode>();
            foreach (var endpoint in new[] { "user", "moderator", "admin" })
            {
                cases.Add(endpoint, AppRoles.User, endpoint == "user" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
                cases.Add(endpoint, AppRoles.Moderator, endpoint != "admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
                cases.Add(endpoint, AppRoles.Admin, HttpStatusCode.OK);
                foreach (var invalid in new[] { "missing", "expired", "tampered" })
                    cases.Add(endpoint, invalid, HttpStatusCode.Unauthorized);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(AccessMatrix))]
    public async Task DevelopmentEndpoint_WithRoleOrInvalidToken_EnforcesPolicyAndProblemDetails(
        string endpoint, string identity, HttpStatusCode expected)
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();
        Assert.Equal(Environments.Development, factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        if (identity != "missing")
        {
            var token = await LoginAsAsync(factory, client, AppRoles.IsValid(identity) ? identity : AppRoles.Admin);
            if (identity == "expired") token = ExpiredToken(factory, token);
            if (identity == "tampered") token = TamperedToken(token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Act
        using var response = await client.GetAsync($"/api/test/{endpoint}");

        // Assert
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("access granted", await response.Content.ReadAsStringAsync());
        }
        else await AssertAuthorizationProblemAsync(response, expected);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task TestEndpoints_OutsideDevelopment_AreNotRegistered(string environment)
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: environment);
        using var client = factory.CreateClient();
        var token = await LoginAsAsync(factory, client, AppRoles.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var paths = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToList();

        // Assert
        Assert.DoesNotContain(paths, path => path?.StartsWith("/api/test/", StringComparison.OrdinalIgnoreCase) == true);
        foreach (var endpoint in new[] { "user", "moderator", "admin" })
        {
            using var response = await client.GetAsync($"/api/test/{endpoint}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void AnonymousEndpoints_MatchExactExplicitWhitelist(string environment)
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: environment);
        using var client = factory.CreateClient();
        string[] expected =
        [
            "* /health",
            "POST /api/auth/register", "POST /api/auth/login", "POST /api/auth/refresh",
            "POST /api/auth/forgot-password", "POST /api/auth/reset-password",
            "POST /api/auth/confirm-email", "POST /api/auth/resend-confirmation",
        ];

        // Act: compare every anonymous endpoint, including verb, without filtering out infrastructure or tests.
        var anonymous = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => Assert.IsAssignableFrom<RouteEndpoint>(endpoint))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText?.TrimStart('/').ToLowerInvariant()}"))
            .OrderBy(endpoint => endpoint, StringComparer.Ordinal).ToArray();

        // Assert
        Assert.Equal(expected.OrderBy(endpoint => endpoint, StringComparer.Ordinal), anonymous);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    [InlineData("confirm-email")]
    [InlineData("resend-confirmation")]
    public async Task PublicAuthEndpoint_WithoutToken_ReachesValidationInsteadOfChallenge(string endpoint)
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync($"/api/auth/{endpoint}", new { });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task EndpointWithoutAttributes_RequiresAuthenticationByDefaultAndAcceptsAuthenticatedUser()
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var anonymous = await client.GetAsync("/__tests/fallback");
        var token = await LoginAsAsync(factory, client, AppRoles.User);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var authenticated = await client.GetAsync("/__tests/fallback");

        // Assert
        await AssertAuthorizationProblemAsync(anonymous, HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText?.Trim('/') == "__tests/fallback");
        Assert.Null(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Logout_WithoutToken_RemainsProtected()
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = "test" });

        // Assert
        await AssertAuthorizationProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Infrastructure_WithoutToken_RemainsAvailable(string path)
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    private static async Task<string> LoginAsAsync(LoginApiFactory factory, HttpClient client, string role)
    {
        await factory.CreateUserAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(LoginApiFactory.Email))!;
            Assert.True((await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        using var response = await client.PostAsJsonAsync("/api/auth/login", EmailConfirmationTestContext.Login());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
    }

    private static string ExpiredToken(LoginApiFactory factory, string original)
    {
        var settings = factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        // Keep the real login claims, issuer, audience and signature, changing only lifetime.
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(original).Claims
            .Where(claim => claim.Type is not "exp" and not "nbf" and not "iat" and not "iss" and not "aud")
            .Select(claim => new Claim(claim.Type, claim.Value));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: settings.Issuer, audience: settings.Audience, claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-20), expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)),
                SecurityAlgorithms.HmacSha256)));
    }

    private static string TamperedToken(string original)
    {
        var parts = original.Split('.');
        var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1;
        parts[2] = Base64UrlEncoder.Encode(signature);
        return string.Join('.', parts);
    }

    private static async Task AssertAuthorizationProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Equal((int)expected, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
        Assert.Equal(expected == HttpStatusCode.Unauthorized ? "Unauthorized" : "Forbidden", problem.Title);
        Assert.Equal(expected == HttpStatusCode.Unauthorized ? "A valid access token is required."
            : "You do not have permission to access this resource.", problem.Detail);
        if (expected == HttpStatusCode.Unauthorized)
            Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        else Assert.Empty(response.Headers.WwwAuthenticate);
    }
}
