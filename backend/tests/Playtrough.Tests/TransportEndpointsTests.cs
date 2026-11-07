using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class TransportEndpointsTests
{
    [Theory]
    [InlineData("Development", "/health", 200)]
    [InlineData("Production", "/health", 200)]
    [InlineData("Production", "/api/admin/users", 401)]
    public async Task Responses_IncludeSecurityHeaders(string environment, string path, int status)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(status, (int)response.StatusCode);
        AssertHeaders(response);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task SwaggerUi_DoesNotReceiveApiCsp()
    {
        using var factory = new LoginApiFactory(environment: "Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task HttpsRedirect_OnlyOutsideDevelopment_UsesConfiguredPort(string environment, bool redirects)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Transport:HttpsPort"] = "8443" })));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://api.example"), AllowAutoRedirect = false,
        });
        using var response = await client.GetAsync("/health");
        Assert.Equal(redirects ? HttpStatusCode.TemporaryRedirect : HttpStatusCode.OK, response.StatusCode);
        if (redirects) Assert.Equal("https://api.example:8443/health", response.Headers.Location?.ToString());
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        AssertHeaders(response);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task Hsts_OnlyOutsideDevelopment_OnHttps(string environment, bool enabled)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example") });
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(enabled, response.Headers.Contains("Strict-Transport-Security"));
        if (enabled) Assert.Equal("max-age=2592000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task KnownLengthBody_OverConfiguredLimit_Returns413WithSecurityHeaders()
    {
        using var factory = new LoginApiFactory();
        using var configured = WithBodyLimit(factory, 256);
        using var client = configured.CreateClient();
        using var response = await client.PostAsync("/api/auth/login", new StringContent(LoginBody(400), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        AssertHeaders(response);
        using var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("payload_too_large", problem!.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Kestrel_EnforcesConfiguredBodyLimit_ForKnownAndUnknownLength(bool chunked)
    {
        using var factory = new LoginApiFactory(environment: "Development");
        using var configured = WithBodyLimit(factory, 256);
        configured.UseKestrel(0);
        // Start first so ClientOptions contains Kestrel's dynamically assigned URL.
        configured.StartServer();
        using var client = configured.CreateClient();

        using var small = await client.PostAsync("/api/auth/login", Content(LoginBody(20), chunked));
        Assert.Equal(HttpStatusCode.Unauthorized, small.StatusCode);
        Assert.False(small.Headers.Contains("Server"));
        using var large = await client.PostAsync("/api/auth/login", Content(LoginBody(400), chunked));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.False(large.Headers.Contains("Server"));
        AssertHeaders(large);
        using var problem = await large.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("payload_too_large", problem!.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Transport:MaxRequestBodySizeBytes", "0")]
    [InlineData("Transport:HttpsPort", "65536")]
    [InlineData("Transport:ForwardLimit", "0")]
    [InlineData("Transport:KnownProxies:0", "invalid-test-proxy")]
    [InlineData("Transport:KnownNetworks:0", "invalid-test-network")]
    public void Startup_RejectsInvalidTransportConfiguration(string key, string value)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains(key.Split(':')[1], error.Message);
        Assert.DoesNotContain("invalid-test-", error.ToString());
    }

    internal static void AssertHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    private static WebApplicationFactory<Program> WithBodyLimit(LoginApiFactory factory, int limit) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<TransportSettings>(settings => settings.MaxRequestBodySizeBytes = limit)));

    private static string LoginBody(int padding) => JsonSerializer.Serialize(new { identifier = "unknown-test-user", password = "test-only-password", padding = new string('a', padding) });

    private static HttpContent Content(string json, bool chunked)
    {
        if (!chunked) return new StringContent(json, Encoding.UTF8, "application/json");
        var content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(json)));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return content;
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
