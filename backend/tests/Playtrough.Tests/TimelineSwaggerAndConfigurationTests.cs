using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class TimelineSwaggerAndConfigurationTests
{
    [Fact]
    public async Task Swagger_DocumentsAllReadEndpointsParametersAndProblemCodes()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/timeline/me", "/api/timeline/me/periods", "/api/users/{username}/timeline", "/api/users/{username}/timeline/periods" })
        {
            var get = paths.GetProperty(path).GetProperty("get");
            Assert.Contains("Timeline", get.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
            Assert.True(get.GetProperty("responses").GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
            Assert.Contains("invalid_filter", get.GetProperty("responses").GetProperty("400").GetProperty("description").GetString());
        }
        var parameters = paths.GetProperty("/api/timeline/me").GetProperty("get").GetProperty("parameters").EnumerateArray().ToArray();
        var limit = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "limit").GetProperty("schema");
        Assert.Equal(20, limit.GetProperty("default").GetInt32());
        Assert.Equal(50, limit.GetProperty("maximum").GetInt32());
        Assert.Contains("history_restricted", paths.GetProperty("/api/users/{username}/timeline").GetProperty("get")
            .GetProperty("responses").GetProperty("403").GetProperty("description").GetString());
    }

    [Fact]
    public void Startup_RejectsInvalidTimelineReadRateLimit()
    {
        using var c = new GamesApiTestContext();
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(s => s.TimelineRead = new(0, TimeSpan.Zero))));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
