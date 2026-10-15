using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Playtrough.Api.Dtos.Auth;
using Xunit;

namespace Playtrough.Tests;

public class LoginIntegrationTests
{
    [Fact]
    public async Task Login_FifthFailedAttemptLocksAccountForFifteenMinutes()
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await LoginAsync(client, "IncorrectPassword1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(attempt, (await factory.ReadUserAsync()).AccessFailedCount);
        }

        var beforeFifthAttempt = DateTimeOffset.UtcNow;
        using var fifth = await LoginAsync(client, "IncorrectPassword1");
        await AssertProblemAsync(fifth, HttpStatusCode.Locked);
        var user = await factory.ReadUserAsync();
        Assert.True(user.LockoutEnabled);
        Assert.NotNull(user.LockoutEnd);
        Assert.InRange(user.LockoutEnd.Value, beforeFifthAttempt.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.Empty(await factory.ReadRefreshTokensAsync());

        // Even a correct password must not bypass an active lockout.
        using var correctWhileLocked = await LoginAsync(client, LoginApiFactory.Password);
        await AssertProblemAsync(correctWhileLocked, HttpStatusCode.Locked);
        Assert.Empty(await factory.ReadRefreshTokensAsync());
    }

    [Fact]
    public async Task Login_SuccessAfterFailedAttemptsPersistsZeroCounterAndRefreshHash()
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var failure = await LoginAsync(client, "IncorrectPassword1");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        Assert.Equal(3, (await factory.ReadUserAsync()).AccessFailedCount);

        using var success = await LoginAsync(client, LoginApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var tokens = await success.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.Equal(0, (await factory.ReadUserAsync()).AccessFailedCount);
        var stored = Assert.Single(await factory.ReadRefreshTokensAsync());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokens.RefreshToken))), stored.TokenHash);
        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), stored.ExpiresAt - stored.CreatedAt);

        // The next failure must start again at one, rather than continuing toward lockout.
        using var nextFailure = await LoginAsync(client, "IncorrectPassword1");
        Assert.Equal(HttpStatusCode.Unauthorized, nextFailure.StatusCode);
        Assert.Equal(1, (await factory.ReadUserAsync()).AccessFailedCount);
    }

    [Fact]
    public async Task Jwt_FromLoginAuthenticatesRealClaimsAndRoleOnProtectedEndpoint()
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var tokens = await SuccessfulLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await client.GetAsync("/__tests/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var identity = await response.Content.ReadFromJsonAsync<ProtectedUserResponse>();
        Assert.NotNull(identity);
        Assert.Equal((await factory.ReadUserAsync()).Id.ToString(CultureInfo.InvariantCulture), identity.Subject);
        Assert.Equal(LoginApiFactory.Username, identity.Username);
        Assert.Equal(LoginApiFactory.Email, identity.Email);
        Assert.Equal("User", identity.Role);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("expired")]
    public async Task Jwt_TamperedOrExpiredTokenIsRejectedByRealMiddleware(string invalid)
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var tokens = await SuccessfulLoginAsync(client);
        var invalidToken = invalid == "tampered"
            ? TamperPayloadWithoutSigning(tokens.AccessToken)
            : ExpireWithValidSignature(tokens.AccessToken, factory.SigningKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalidToken);

        using var response = await client.GetAsync("/__tests/protected");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
    }

    [Fact]
    public async Task Login_EmptyFieldsReturn400ThroughTheHttpValidationPipeline()
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(LoginRequestDto.Identifier), problem.Errors.Keys);
        Assert.Contains(nameof(LoginRequestDto.Password), problem.Errors.Keys);
        Assert.Equal(0, (await factory.ReadUserAsync()).AccessFailedCount);
        Assert.Empty(await factory.ReadRefreshTokensAsync());
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
        => client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Identifier = LoginApiFactory.Username,
            Password = password,
        });

    private static async Task<LoginResponseDto> SuccessfulLoginAsync(HttpClient client)
    {
        using var response = await LoginAsync(client, LoginApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(tokens);
        return tokens;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)status, problem.Status);
    }

    private static string TamperPayloadWithoutSigning(string token)
    {
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.DecodeBytes(parts[1]))!.AsObject();
        payload[JwtRegisteredClaimNames.Sub] = "99999";
        return $"{parts[0]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload.ToJsonString()))}.{parts[2]}";
    }

    private static string ExpireWithValidSignature(string token, string key)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var claims = jwt.Claims.Where(claim => claim.Type is not "nbf" and not "exp" and not "iss" and not "aud");
        var expired = new JwtSecurityToken(jwt.Issuer, jwt.Audiences.Single(), claims,
            notBefore: DateTime.UtcNow.AddMinutes(-16), expires: DateTime.UtcNow.AddSeconds(-1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(expired);
    }
}
