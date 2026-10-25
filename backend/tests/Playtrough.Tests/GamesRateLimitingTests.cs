using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Playtrough.Tests;

public class GamesRateLimitingTests
{
    [Fact]
    public async Task DefaultCatalogConfiguration_AllowsThirtyRequestsThenRejects()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: null);
        using var client = await context.AuthenticatedClientAsync();
        for (var index = 0; index < 30; index++)
        {
            using var allowed = await client.GetAsync("/api/games/search?q=witcher");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var rejected = await client.GetAsync("/api/games/search?q=witcher");
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.Equal(30, context.Provider.SearchCalls);
    }

    [Fact]
    public async Task ChangingPeerIp_DoesNotResetTheSameUsersBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 1);
        using var client = await context.AuthenticatedClientAsync();
        using var first = new HttpRequestMessage(HttpMethod.Get, "/api/games/search?q=witcher");
        first.Headers.Add("X-Test-Remote-IP", "203.0.113.11");
        using var second = new HttpRequestMessage(HttpMethod.Get, "/api/games/search?q=witcher");
        second.Headers.Add("X-Test-Remote-IP", "203.0.113.12");
        using var allowed = await client.SendAsync(first);
        using var rejected = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task CatalogBudget_IsSharedBySearchAndDetail_AndReturnsRetryAfter()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var client = await context.AuthenticatedClientAsync();
        using var search = await client.GetAsync("/api/games/search?q=witcher");
        using var detail = await client.GetAsync("/api/games/19");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        foreach (var path in new[] { "/api/games/search?q=witcher", "/api/games/19" })
        {
            using var rejected = await client.GetAsync(path);
            await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
            Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        }
        Assert.Equal(1, context.Provider.SearchCalls);
        Assert.Equal(1, context.Provider.DetailCalls);
    }

    [Fact]
    public async Task DifferentUsers_OnSamePeerHaveIndependentBudgets()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 1);
        using var first = await context.AuthenticatedClientAsync();
        using var second = await context.AuthenticatedClientAsync("Admin");
        using var allowed = await first.GetAsync("/api/games/search?q=witcher");
        using var rejected = await first.GetAsync("/api/games/search?q=witcher");
        using var independent = await second.GetAsync("/api/games/search?q=witcher");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
    }

    [Fact]
    public async Task NewTokenForSameUser_DoesNotResetBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 1);
        using var first = await context.AuthenticatedClientAsync();
        using var second = await context.LoginClientAsync();
        Assert.NotEqual(first.DefaultRequestHeaders.Authorization!.Parameter, second.DefaultRequestHeaders.Authorization!.Parameter);
        using var allowed = await first.GetAsync("/api/games/search?q=witcher");
        using var rejected = await second.GetAsync("/api/games/19");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Fact]
    public async Task AnonymousAndTamperedTokens_AlwaysGet401_AndDoNotConsumeClaimedUsersBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 1);
        using var authenticated = await context.AuthenticatedClientAsync();
        using var anonymous = context.Factory.CreateClient();
        using var tampered = context.Factory.CreateClient();
        tampered.DefaultRequestHeaders.Authorization = new("Bearer", authenticated.DefaultRequestHeaders.Authorization!.Parameter + "x");
        for (var index = 0; index < 3; index++)
        {
            using var noToken = await anonymous.GetAsync("/api/games/search?q=witcher");
            using var badToken = await tampered.GetAsync("/api/games/19");
            Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, badToken.StatusCode);
        }
        using var allowed = await authenticated.GetAsync("/api/games/search?q=witcher");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var rejected = await authenticated.GetAsync("/api/games/search?q=witcher");
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        using var afterLimit = await anonymous.GetAsync("/api/games/19");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLimit.StatusCode);
    }

    [Fact]
    public async Task InvalidQueries_ConsumeAuthenticatedBudget_AndDisabledLimitsAllowRepeatedCalls()
    {
        using (var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 1))
        {
            using var client = await context.AuthenticatedClientAsync();
            using var invalid = await client.GetAsync("/api/games/search?q=x");
            await GamesEndpointsTests.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "invalid_query");
            using var rejected = await client.GetAsync("/api/games/search?q=witcher");
            await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        }
        using var disabled = new GamesApiTestContext(catalogLimit: 1);
        using var unlimited = await disabled.AuthenticatedClientAsync();
        for (var index = 0; index < 4; index++)
        {
            using var allowed = await unlimited.GetAsync("/api/games/search?q=witcher");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
    }

    [Theory]
    [InlineData("PermitLimit", "0")]
    [InlineData("Window", "00:00:00")]
    [InlineData("Window", "test-only-catalog-canary")]
    public void InvalidCatalogRateConfiguration_IsRejectedSafely(string property, string value)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"RateLimit:Catalog:{property}"] = value })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("RateLimit", error.Message);
        Assert.DoesNotContain("test-only-catalog-canary", error.ToString());
    }
}
