using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class ForwardedHeadersTests
{
    [Theory]
    [InlineData("", "", "203.0.113.9", false)]
    [InlineData("", "", "127.0.0.1", false)]
    [InlineData("10.0.0.10", "", "203.0.113.9", false)]
    [InlineData("10.0.0.10", "", "10.0.0.10", true)]
    [InlineData("", "10.20.0.0/16", "10.20.1.2", true)]
    [InlineData("", "10.20.0.0/16", "10.21.1.2", false)]
    [InlineData("::1", "", "::1", true)]
    public async Task Headers_OnlyChangeConnectionForExplicitTrustedPeers(string proxy, string network, string remoteIp, bool trusted)
    {
        using var factory = new LoginApiFactory(environment: "Development", includeSecurityEndpoints: true);
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<TransportSettings>(settings =>
            {
                settings.KnownProxies = proxy.Length == 0 ? [] : [proxy];
                settings.KnownNetworks = network.Length == 0 ? [] : [network];
            });
            services.AddSingleton<IStartupFilter>(new RemoteIpFilter(remoteIp));
        }));
        using var client = configured.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/__tests/security/connection");
        request.Headers.Add("X-Forwarded-For", "198.51.100.42");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        using var connection = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(trusted ? "198.51.100.42" : remoteIp, connection!.RootElement.GetProperty("ip").GetString());
        Assert.Equal(trusted ? "https" : "http", connection.RootElement.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task TrustedProxy_UpdatesSchemeBeforeProductionHttpsRedirect()
    {
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<TransportSettings>(settings => settings.KnownProxies = ["10.0.0.10"]);
            services.AddSingleton<IStartupFilter>(new RemoteIpFilter("10.0.0.10"));
        }));
        using var client = configured.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("http://api.example") });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-For", "198.51.100.42");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task AutomaticForwardingSetting_DoesNotEnableTrustWithoutAllowlist()
    {
        using var factory = new LoginApiFactory(environment: "Development", includeSecurityEndpoints: true);
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ForwardedHeaders_Enabled", "true");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new RemoteIpFilter("127.0.0.1")));
        });
        using var client = configured.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/__tests/security/connection");
        request.Headers.Add("X-Forwarded-For", "198.51.100.42");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        using var connection = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("127.0.0.1", connection!.RootElement.GetProperty("ip").GetString());
        Assert.Equal("http", connection.RootElement.GetProperty("scheme").GetString());
    }

    // TestServer has no real TCP peer. Fix it before the production middleware,
    // independently of request headers so a caller cannot choose its trusted status.
    private sealed class RemoteIpFilter(string ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                return continuation(context);
            });
            next(app);
        };
    }
}
