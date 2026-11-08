using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class UserSearchEndpointsTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task Search_AllRoles_ExactPublicShapeAndLiteralRoute(string role)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync(role);
        using var other = await c.AuthenticatedClientAsync();
        using var result = (await client.GetFromJsonAsync<JsonDocument>("/api/users/search?q=GAMES_"))!;
        var root = result.RootElement;
        Assert.Equal(new[] { "hasMore", "items", "page", "pageSize" }, root.EnumerateObject().Select(p => p.Name).Order());
        var row = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(new[] { "avatarUrl", "relationship", "username" }, row.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("games_user_2", row.GetProperty("username").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("avatarUrl").ValueKind);
        Assert.Equal("none", row.GetProperty("relationship").GetProperty("status").GetString());
        Assert.False(row.GetProperty("relationship").TryGetProperty("requestId", out _));
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.False(root.GetProperty("hasMore").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/GAMES_USER_2")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20a%20")]
    [InlineData("?q=%20%20")]
    [InlineData("?q=ab&page=0")]
    [InlineData("?q=ab&pageSize=21")]
    [InlineData("?q=ab&pageSize=0")]
    [InlineData("?q=ab&page=2147483647")]
    [InlineData("?q=ab&page=no")]
    [InlineData("?q=ab&pageSize=no")]
    public async Task InvalidQuery_Stable400(string suffix)
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        await ProfileEndpointsTests.Error(await client.GetAsync("/api/users/search" + suffix), 400, "invalid_query");
    }

    [Fact]
    public async Task TooLongQuery_400_WithoutReflectingInput()
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        var query = new string('x', 51);
        using var response = await client.GetAsync("/api/users/search?q=" + query);
        await ProfileEndpointsTests.Error(response, 400, "invalid_query");
    }

    [Theory]
    [InlineData("?q=ab")]
    [InlineData("")]
    public async Task NoToken_401BeforeValidation(string suffix)
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var client = c.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/search" + suffix)).StatusCode);
    }

    [Fact]
    public async Task RateLimit_30PerUser_RetryAfterAndRenewedTokenSameBudget()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var client = await c.AuthenticatedClientAsync();
        using var other = await c.AuthenticatedClientAsync();
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/search?q=games")).StatusCode);
        using var limited = await client.GetAsync("/api/users/search?q=games");
        await ProfileEndpointsTests.Error(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/users/search?q=games")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        using var renewed = await c.LoginClientAsync();
        renewed.DefaultRequestHeaders.Add("X-Test-Remote-IP", "203.0.113.22");
        await ProfileEndpointsTests.Error(await renewed.GetAsync("/api/users/search?q=games"), 429, "rate_limited");
    }

    [Fact]
    public async Task ConfiguredRateLimit_IsApplied()
    {
        using var c = new GamesApiTestContext(enableLimits: true);
        using var original = await c.AuthenticatedClientAsync();
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.UserSearch = new(1, TimeSpan.FromMinutes(1)))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/search?q=zz")).StatusCode);
        await ProfileEndpointsTests.Error(await client.GetAsync("/api/users/search?q=zz"), 429, "rate_limited");
    }

    [Fact]
    public async Task Swagger_DocumentsRequiredQueryLimitsAndSecurity()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var operation = doc.RootElement.GetProperty("paths").GetProperty("/api/users/search").GetProperty("get");
        Assert.Equal("Users", operation.GetProperty("tags")[0].GetString());
        Assert.Contains("user-search", operation.GetProperty("description").GetString());
        var query = operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString()!.Equals("q", StringComparison.OrdinalIgnoreCase));
        Assert.True(query.GetProperty("required").GetBoolean());
        Assert.Equal(2, query.GetProperty("schema").GetProperty("minLength").GetInt32());
        Assert.Equal(50, query.GetProperty("schema").GetProperty("maxLength").GetInt32());
        var responses = operation.GetProperty("responses");
        foreach (var code in new[] { "200", "400", "401", "429" }) Assert.True(responses.TryGetProperty(code, out _));
        Assert.Contains("invalid_query", responses.GetProperty("400").GetProperty("description").GetString());
        Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        Assert.NotEmpty(operation.GetProperty("security").EnumerateArray());
    }
}
