using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Playtrough.Tests;

public class CorsEndpointsTests
{
    [Theory]
    [InlineData("/api/admin/users", "GET")]
    [InlineData("/api/admin/users/1/role", "PUT")]
    [InlineData("/api/admin/role-changes", "GET")]
    [InlineData("/api/auth/login", "POST")]
    [InlineData("/api/admin/users", "OPTIONS")]
    public async Task Preflight_WithAllowedOrigin_AdvertisesMethodsAndHeadersWithoutAuthentication(string path, string method)
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        using var request = Preflight(path, "http://localhost:4200", method);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:4200", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(new[] { "GET", "OPTIONS", "POST", "PUT" }, HeaderValues(response, "Access-Control-Allow-Methods").OrderBy(value => value));
        Assert.Equal(new[] { "authorization", "content-type" }, HeaderValues(response, "Access-Control-Allow-Headers").Select(value => value.ToLowerInvariant()).OrderBy(value => value));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("/api/admin/users", "GET")]
    [InlineData("/api/admin/users/1/role", "PUT")]
    [InlineData("/api/admin/role-changes", "GET")]
    public async Task Preflight_WithUnlistedOrigin_DoesNotGrantCorsAccess(string path, string method)
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        using var request = Preflight(path, "https://unlisted.example", method);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Preflight_UsesConfiguredFrontendOrigin()
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://frontend.example",
            })));
        using var client = configured.CreateClient();
        foreach (var origin in new[] { "https://frontend.example", "http://localhost:4200" })
        {
            using var request = Preflight("/api/admin/users", origin, "GET");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            if (origin == "https://frontend.example")
                Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            else
                Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    private static HttpRequestMessage Preflight(string path, string origin, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    private static IEnumerable<string> HeaderValues(HttpResponseMessage response, string name) =>
        response.Headers.GetValues(name).SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
