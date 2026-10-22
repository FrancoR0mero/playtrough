using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Playtrough.Api.Models;
using Playtrough.Api.RateLimiting;
using Xunit;

namespace Playtrough.Tests;

public class RateLimitingEndpointsTests
{
    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    [InlineData("forgot-password")]
    [InlineData("resend-confirmation")]
    [InlineData("reset-password")]
    [InlineData("confirm-email")]
    [InlineData("refresh")]
    public async Task AuthPolicies_RejectAfterConfiguredLimit_WithProblemDetailsAndRetryAfter(string endpoint)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();
        var path = $"/api/auth/{endpoint}";

        // Invalid JSON models still consume permits, without creating accounts or tokens.
        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest);
        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest);
        using var rejected = await SendAsync(client, path);
        await AssertRateLimitedAsync(rejected);
        TransportEndpointsTests.AssertHeaders(rejected);
        Assert.Equal("http://localhost:4200", Assert.Single(rejected.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Login_RejectionLogsOnlyPolicyAndIp_WithoutCredentialsOrTokens()
    {
        using var factory = new LoginApiFactory();
        var logger = new Mock<ILogger<ConfigureRateLimiting>>();
        using var limited = WithLimits(factory, settings => settings.Login.PermitLimit = 1,
            services => services.AddSingleton(logger.Object));
        using var client = limited.CreateClient();
        var body = new { identifier = "private-user-canary", password = "private-password-canary" };
        using var first = await SendAsync(client, "/api/auth/login", body: body);
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        using var rejected = await SendAsync(client, "/api/auth/login", body: body, token: "private-access-canary");
        await AssertRateLimitedAsync(rejected);

        var log = Assert.Single(logger.Invocations, call => call.Method.Name == "Log");
        Assert.Equal(LogLevel.Warning, log.Arguments[0]);
        Assert.Null(log.Arguments[3]);
        var state = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(log.Arguments[2]);
        Assert.Equal("login", state.Single(pair => pair.Key == "Policy").Value);
        Assert.Equal("203.0.113.10", state.Single(pair => pair.Key == "IpAddress").Value);
        Assert.Equal(3, state.Count()); // Policy, IpAddress and the constant message template.
        Assert.DoesNotContain("canary", log.Arguments[2]?.ToString() ?? string.Empty);
    }

    [Theory]
    [InlineData("forgot-password", "resend-confirmation")]
    [InlineData("reset-password", "confirm-email")]
    public async Task GroupedEndpoints_ShareTheirPolicyBudget(string first, string second)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();

        await AssertStatusAsync(client, $"/api/auth/{first}", HttpStatusCode.BadRequest);
        await AssertStatusAsync(client, $"/api/auth/{second}", HttpStatusCode.BadRequest);
        using var rejected = await SendAsync(client, $"/api/auth/{first}");
        await AssertRateLimitedAsync(rejected);
    }

    [Fact]
    public async Task NamedPolicies_HaveIndependentBudgets_AndDoNotConsumeGlobalBudget()
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory, settings => settings.Global.PermitLimit = 1);
        using var client = limited.CreateClient();

        foreach (var endpoint in new[] { "login", "register", "forgot-password", "reset-password", "refresh" })
        {
            var path = $"/api/auth/{endpoint}";
            await AssertStatusAsync(client, path, HttpStatusCode.BadRequest);
            await AssertStatusAsync(client, path, HttpStatusCode.BadRequest);
            using var rejected = await SendAsync(client, path);
            await AssertRateLimitedAsync(rejected);
        }
        await AssertStatusAsync(client, "/health", HttpStatusCode.OK);
        using var globalRejection = await SendAsync(client, "/health");
        await AssertRateLimitedAsync(globalRejection);
    }

    [Theory]
    [InlineData("/api/auth/login", HttpStatusCode.BadRequest)]
    [InlineData("/health", HttpStatusCode.OK)]
    public async Task IpPartitions_AreIndependent_AndIgnoreUntrustedForwardedHeaders(string path, HttpStatusCode accepted)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();

        // Vary the claimed forwarding address on every request. Without trusted
        // proxies, only the test-controlled transport peer determines the budget.
        await AssertStatusAsync(client, path, accepted, ip: "203.0.113.1", forwardedIp: "198.51.100.1");
        await AssertStatusAsync(client, path, accepted, ip: "203.0.113.1", forwardedIp: "198.51.100.2");
        using var rejected = await SendAsync(client, path, ip: "203.0.113.1", forwardedIp: "198.51.100.3");
        await AssertRateLimitedAsync(rejected);
        await AssertStatusAsync(client, path, accepted, ip: "203.0.113.2", forwardedIp: "198.51.100.1");
    }

    [Fact]
    public async Task TrustedForwarding_IsProcessedBeforeSelectingIpPartition()
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory, configureServices: services =>
            services.PostConfigure<TransportSettings>(settings => settings.KnownProxies = ["10.0.0.10"]));
        using var client = limited.CreateClient();
        const string path = "/api/auth/login";

        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest, ip: "10.0.0.10", forwardedIp: "198.51.100.1");
        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest, ip: "10.0.0.10", forwardedIp: "198.51.100.1");
        using var rejected = await SendAsync(client, path, ip: "10.0.0.10", forwardedIp: "198.51.100.1");
        await AssertRateLimitedAsync(rejected);
        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest, ip: "10.0.0.10", forwardedIp: "198.51.100.2");
    }

    [Theory]
    [InlineData("/api/auth/login", HttpStatusCode.BadRequest)]
    [InlineData("/health", HttpStatusCode.OK)]
    public async Task Ipv4AndItsMappedIpv6Representation_ShareBudget(string path, HttpStatusCode accepted)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();
        await AssertStatusAsync(client, path, accepted, ip: "203.0.113.1");
        await AssertStatusAsync(client, path, accepted, ip: "::ffff:203.0.113.1");
        using var rejected = await SendAsync(client, path, ip: "203.0.113.1");
        await AssertRateLimitedAsync(rejected);
    }

    [Theory]
    [InlineData("/api/auth/login", HttpStatusCode.BadRequest)]
    [InlineData("/health", HttpStatusCode.OK)]
    public async Task AfterWindowExpires_RequestsAreAcceptedAgain(string path, HttpStatusCode accepted)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory, settings =>
        {
            settings.Login = new(1, TimeSpan.FromSeconds(1));
            settings.Global = new(1, TimeSpan.FromSeconds(1));
        });
        using var client = limited.CreateClient();
        await AssertStatusAsync(client, path, accepted);
        using var rejected = await SendAsync(client, path);
        await AssertRateLimitedAsync(rejected);

        // The framework replenishes partitioned windows on its own timer. Poll with
        // a deadline so scheduler jitter does not require an exact timer boundary.
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            await Task.Delay(100);
            using var renewed = await SendAsync(client, path);
            if (renewed.StatusCode == HttpStatusCode.TooManyRequests && elapsed.Elapsed < TimeSpan.FromSeconds(8)) continue;
            Assert.Equal(accepted, renewed.StatusCode);
            break;
        }
    }

    [Fact]
    public async Task GlobalLimit_UsesValidatedUserId_AcrossIpsAndTokens_WithoutSharingBetweenUsers()
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();
        var first = Token(factory, "101");
        var renewed = Token(factory, "101");
        var second = Token(factory, "202");
        const string path = "/__tests/protected";

        await AssertStatusAsync(client, path, HttpStatusCode.OK, token: first);
        await AssertStatusAsync(client, path, HttpStatusCode.OK, token: renewed, ip: "203.0.113.20");
        using var rejected = await SendAsync(client, path, token: first, ip: "203.0.113.30");
        await AssertRateLimitedAsync(rejected);
        await AssertStatusAsync(client, path, HttpStatusCode.OK, token: second);
        // Anonymous clients behind the same address have a separate IP partition.
        await AssertStatusAsync(client, "/health", HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/admin/users")]
    [InlineData("/__tests/fallback")]
    public async Task GlobalLimit_AppliesToProtectedEndpoints_BeforeAuthorization(string path)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();
        await AssertStatusAsync(client, path, HttpStatusCode.Unauthorized);
        await AssertStatusAsync(client, path, HttpStatusCode.Unauthorized);
        using var rejected = await SendAsync(client, path);
        await AssertRateLimitedAsync(rejected);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("unsigned")]
    public async Task InvalidTokens_CannotChooseUserPartitionsByChangingSubject(string invalid)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();
        const string path = "/__tests/protected";
        await AssertStatusAsync(client, path, HttpStatusCode.Unauthorized, token: Token(factory, "101", invalid));
        await AssertStatusAsync(client, path, HttpStatusCode.Unauthorized, token: Token(factory, "202", invalid));
        using var rejected = await SendAsync(client, path, token: Token(factory, "303", invalid));
        await AssertRateLimitedAsync(rejected);
    }

    [Fact]
    public async Task AuthenticatedClients_CannotBypassNamedIpPolicyByChangingTokens()
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory);
        using var client = limited.CreateClient();
        const string path = "/api/auth/login";
        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest, token: Token(factory, "101"));
        await AssertStatusAsync(client, path, HttpStatusCode.BadRequest, token: Token(factory, "202"));
        using var rejected = await SendAsync(client, path, token: Token(factory, "303"));
        await AssertRateLimitedAsync(rejected);
    }

    [Fact]
    public async Task CorsPreflight_RemainsAvailableAfterRateBudgetIsExhausted()
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory, settings => settings.Global.PermitLimit = 1);
        using var client = limited.CreateClient();
        await AssertStatusAsync(client, "/api/admin/users", HttpStatusCode.Unauthorized);
        using var rejected = await SendAsync(client, "/api/admin/users");
        await AssertRateLimitedAsync(rejected);

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/users");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:4200", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task RateLimit_DoesNotReplaceIdentityAccountLockout()
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory, settings => settings.Login.PermitLimit = 6);
        using var client = limited.CreateClient();
        await factory.CreateUserAsync();
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            using var response = await SendAsync(client, "/api/auth/login",
                body: new { identifier = LoginApiFactory.Username, password = "IncorrectPassword1" });
            Assert.Equal(attempt < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.Locked, response.StatusCode);
        }
        using var rejected = await SendAsync(client, "/api/auth/login",
            body: new { identifier = LoginApiFactory.Username, password = LoginApiFactory.Password });
        await AssertRateLimitedAsync(rejected);
        Assert.True((await factory.ReadUserAsync()).LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("/api/auth/login", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/register", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/forgot-password", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/resend-confirmation", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/reset-password", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/confirm-email", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/refresh", HttpStatusCode.BadRequest)]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/__tests/fallback", HttpStatusCode.Unauthorized)]
    public async Task Disabled_DoesNotLimitAnyPolicy(string path, HttpStatusCode expected)
    {
        using var factory = new LoginApiFactory();
        using var limited = WithLimits(factory, settings => settings.Enabled = false);
        using var client = limited.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
            await AssertStatusAsync(client, path, expected);
    }

    private static WebApplicationFactory<Program> WithLimits(LoginApiFactory factory,
        Action<RateLimitSettings>? configure = null, Action<IServiceCollection>? configureServices = null)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<RateLimitSettings>(settings =>
            {
                settings.Enabled = true;
                settings.Login = new(2, TimeSpan.FromMinutes(1));
                settings.Register = new(2, TimeSpan.FromMinutes(1));
                settings.PasswordRecovery = new(2, TimeSpan.FromMinutes(1));
                settings.TokenActions = new(2, TimeSpan.FromMinutes(1));
                settings.Refresh = new(2, TimeSpan.FromMinutes(1));
                settings.Global = new(2, TimeSpan.FromMinutes(1));
                configure?.Invoke(settings);
            });
            services.PostConfigure<TransportSettings>(settings =>
            {
                settings.KnownProxies = [];
                settings.KnownNetworks = [];
            });
            services.AddSingleton<IStartupFilter, RateLimitRemoteIpFilter>();
            configureServices?.Invoke(services);
        }));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path,
        string ip = "203.0.113.10", string? token = null, string? forwardedIp = null, object? body = null)
    {
        var method = path.StartsWith("/api/auth/", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get;
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post) request.Content = JsonContent.Create(body ?? new { });
        request.Headers.Add("X-Test-Remote-IP", ip);
        request.Headers.Add("Origin", "http://localhost:4200");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (forwardedIp is not null) request.Headers.Add("X-Forwarded-For", forwardedIp);
        return await client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(HttpClient client, string path, HttpStatusCode expected,
        string ip = "203.0.113.10", string? token = null, string? forwardedIp = null)
    {
        using var response = await SendAsync(client, path, ip, token, forwardedIp);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(429, problem!.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("rate_limited", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("Demasiadas solicitudes", problem.RootElement.GetProperty("title").GetString());
    }

    private static string Token(LoginApiFactory factory, string subject, string? invalid = null)
    {
        var key = invalid == "tampered" ? new string('z', 64) : factory.SigningKey;
        var token = new JwtSecurityToken("Playtrough.Tests", "Playtrough.Tests.Client",
            [new Claim("sub", subject), new Claim("role", "User"), new Claim("jti", Guid.NewGuid().ToString())],
            expires: invalid == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: invalid == "unsigned" ? null :
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class RateLimitRemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // Only registered in these tests. TestServer has no TCP peer; this
            // header sets it before the real forwarding and rate-limit pipeline.
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(
                    context.Request.Headers["X-Test-Remote-IP"].FirstOrDefault() ?? "203.0.113.10");
                return continuation(context);
            });
            next(app);
        };
    }
}
