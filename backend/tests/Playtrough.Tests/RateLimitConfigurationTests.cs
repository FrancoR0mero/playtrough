using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class RateLimitConfigurationTests
{
    [Theory]
    [InlineData("Login", "PermitLimit", "0")]
    [InlineData("Register", "PermitLimit", "-1")]
    [InlineData("PasswordRecovery", "PermitLimit", "0")]
    [InlineData("TokenActions", "PermitLimit", "0")]
    [InlineData("Refresh", "PermitLimit", "0")]
    [InlineData("Global", "PermitLimit", "0")]
    [InlineData("Login", "Window", "00:00:00")]
    [InlineData("Register", "Window", "-00:00:01")]
    [InlineData("PasswordRecovery", "Window", "00:00:00")]
    [InlineData("TokenActions", "Window", "00:00:00")]
    [InlineData("Refresh", "Window", "00:00:00")]
    [InlineData("Global", "Window", "00:00:00")]
    public void Startup_RejectsNonPositiveLimitsAndWindows(string policy, string property, string value)
    {
        var key = $"RateLimit:{policy}:{property}";
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains(key, error.Message);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Login:PermitLimit")]
    [InlineData("Global:Window")]
    public void Startup_RejectsInvalidValueTypes_WithoutEchoingValues(string property)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"RateLimit:{property}"] = "test-only-invalid-canary" })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("RateLimit", error.Message);
        Assert.DoesNotContain("canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Production_BindsPolicyLimitsFromConfiguration()
    {
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimit:Login:PermitLimit"] = "1",
                    ["RateLimit:Login:Window"] = "00:00:30",
                }));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<RateLimitSettings>(settings => settings.Enabled = true));
        });
        using var client = configured.CreateClient();
        using var first = await client.PostAsJsonAsync("/api/auth/login", new { });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/api/auth/login", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 30);
    }
}
