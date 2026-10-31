using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class ReviewsRateLimitingTests
{
    [Fact]
    public async Task DefaultWritePolicy_Allows20ThenRejects_WithRetryAfter_AndLeavesReadsAvailable()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var client = await context.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(client);
        var path = ReviewEndpointsTests.PathFor(game.Id);
        for (var index = 0; index < 20; index++)
        {
            using var allowed = await client.PutAsJsonAsync(path, new { rating = 4.5m });
            Assert.Equal(index == 0 ? HttpStatusCode.Created : HttpStatusCode.OK, allowed.StatusCode);
        }
        using var rejected = await client.DeleteAsync(path);
        await AssertLimited(rejected);
        using var read = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(1, (await ReviewEndpointsTests.OpenGameAsync(client)).RatingSummary.Count);
        await ReviewEndpointsTests.AssertStateAsync(context, game.Id, 4.5m);
    }

    [Fact]
    public async Task WriteBudget_IsSharedByPutDelete_AndUserIdentitySurvivesNewTokenAndIp()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var original = await context.AuthenticatedClientAsync();
        using var otherOriginal = await context.AuthenticatedClientAsync("Admin");
        var game = await ReviewEndpointsTests.OpenGameAsync(original);
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.ReviewsWrite = new(2, TimeSpan.FromMinutes(1)))));
        using var client = factory.CreateClient();
        using var other = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        other.DefaultRequestHeaders.Authorization = otherOriginal.DefaultRequestHeaders.Authorization;
        var path = ReviewEndpointsTests.PathFor(game.Id);
        using var created = await client.PutAsJsonAsync(path, new { rating = 4.5m });
        using var deleted = await client.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var renewed = await context.LoginClientAsync();
        client.DefaultRequestHeaders.Authorization = renewed.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "203.0.113.77");
        using var rejected = await client.PutAsJsonAsync(path, new { rating = 3m });
        await AssertLimited(rejected);
        using var independent = await other.PutAsJsonAsync(path, new { rating = 2.5m });
        Assert.Equal(HttpStatusCode.Created, independent.StatusCode);
        using var ownRead = await client.GetAsync(path);
        await GamesEndpointsTests.AssertProblemAsync(ownRead, HttpStatusCode.NotFound, "review_not_found");
        Assert.Equal(1, (await ReviewEndpointsTests.OpenGameAsync(client)).RatingSummary.Count);
        await ReviewEndpointsTests.AssertStateAsync(context, game.Id, 2.5m);
    }

    [Fact]
    public async Task AnonymousOrInvalidTokens_Stay401_AndCannotConsumeAUsersWriteBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var authenticated = await context.AuthenticatedClientAsync();
        var game = await ReviewEndpointsTests.OpenGameAsync(authenticated);
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.ReviewsWrite = new(1, TimeSpan.FromMinutes(1)))));
        using var anonymous = factory.CreateClient();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var path = ReviewEndpointsTests.PathFor(game.Id);
        for (var index = 0; index < 3; index++)
        {
            using var missingToken = await anonymous.PutAsJsonAsync(path, new { rating = 4.5m });
            Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        }
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-test-token");
        using var invalidToken = await anonymous.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
        using var allowed = await client.PutAsJsonAsync(path, new { rating = 4.5m });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        using var rejected = await client.DeleteAsync(path);
        await AssertLimited(rejected);
        anonymous.DefaultRequestHeaders.Authorization = null;
        using var stillUnauthorized = await anonymous.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, stillUnauthorized.StatusCode);
    }

    [Fact]
    public async Task InvalidBodies_ConsumeWriteBudget_AndDisabledLimitsAllowRepeatedWrites()
    {
        using (var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 100))
        {
            using var authenticated = await context.AuthenticatedClientAsync();
            var game = await ReviewEndpointsTests.OpenGameAsync(authenticated);
            using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.PostConfigure<RateLimitSettings>(settings => settings.ReviewsWrite = new(1, TimeSpan.FromMinutes(1)))));
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
            using var invalid = await client.PutAsJsonAsync(ReviewEndpointsTests.PathFor(game.Id), new { });
            await GamesEndpointsTests.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "invalid_rating");
            using var rejected = await client.PutAsJsonAsync(ReviewEndpointsTests.PathFor(game.Id), new { rating = 4.5m });
            await AssertLimited(rejected);
            await ReviewEndpointsTests.AssertStateAsync(context, game.Id);
        }
        using var disabled = new GamesApiTestContext();
        using var original = await disabled.AuthenticatedClientAsync();
        var otherGame = await ReviewEndpointsTests.OpenGameAsync(original);
        using var noLimits = disabled.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.ReviewsWrite = new(1, TimeSpan.FromMinutes(1)))));
        using var unlimited = noLimits.CreateClient();
        unlimited.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        for (var index = 0; index < 4; index++)
        {
            using var allowed = await unlimited.PutAsJsonAsync(ReviewEndpointsTests.PathFor(otherGame.Id), new { rating = 4.5m });
            Assert.Equal(index == 0 ? HttpStatusCode.Created : HttpStatusCode.OK, allowed.StatusCode);
        }
    }

    [Fact]
    public async Task OwnReads_UseExistingGlobalLimit_AndIncludeRetryAfter()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var authenticated = await context.AuthenticatedClientAsync();
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.Global = new(1, TimeSpan.FromMinutes(1)))));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        using var first = await client.GetAsync(ReviewEndpointsTests.PathFor(1));
        await GamesEndpointsTests.AssertProblemAsync(first, HttpStatusCode.NotFound, "review_not_found");
        using var rejected = await client.GetAsync(ReviewEndpointsTests.PathFor(1));
        await AssertLimited(rejected);
    }

    [Theory]
    [InlineData("PermitLimit", "0")]
    [InlineData("PermitLimit", "-1")]
    [InlineData("Window", "00:00:00")]
    [InlineData("Window", "test-only-reviews-canary")]
    public void InvalidWriteRateConfiguration_IsRejectedSafely(string property, string value)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"RateLimit:ReviewsWrite:{property}"] = value })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("RateLimit", error.Message);
        Assert.DoesNotContain("test-only-reviews-canary", error.ToString());
    }

    private static async Task AssertLimited(HttpResponseMessage response)
    {
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.InRange(response.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
    }
}
