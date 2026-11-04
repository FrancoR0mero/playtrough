using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class GamePersistenceEndpointsTests
{
    [Theory]
    [InlineData("Development", false)]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    public async Task MissingProviderGame_MapsTo404WithStableCode_EvenWithStaleCopy(string environment, bool hasCopy)
    {
        using var factory = new LoginApiFactory(environment: environment);
        var provider = new Mock<IGameCatalogProvider>(MockBehavior.Strict);
        provider.Setup(item => item.GetByExternalIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync((GameDetails?)null);
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddControllers().ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new CatalogControllerFeatureProvider()));
            services.RemoveAll<IGameCatalogProvider>();
            services.AddSingleton(provider.Object);
        }));
        using var client = configured.CreateClient();
        if (hasCopy)
        {
            using var scope = configured.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Games.Add(GamePersistenceTestContext.LocalGame(DateTime.UtcNow.AddDays(-31)));
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync("/__tests/catalog/local/42");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("game_not_found", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(nameof(GameNotFoundException), body);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    public async Task ProviderUnavailable_MapsTo503_OrReturnsLocalCopy(string environment, bool hasCopy)
    {
        using var factory = new LoginApiFactory(environment: environment);
        // The default disabled provider is real phase-1 behavior: no network and a typed failure.
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new CatalogControllerFeatureProvider()))));
        using var client = configured.CreateClient();
        if (hasCopy)
        {
            using var scope = configured.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Games.Add(GamePersistenceTestContext.LocalGame(DateTime.UtcNow.AddDays(-31)));
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync("/__tests/catalog/local/42");
        Assert.Equal(hasCopy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (hasCopy)
        {
            Assert.True(result.RootElement.GetProperty("id").GetInt32() > 0);
            Assert.Equal("Old name", result.RootElement.GetProperty("name").GetString());
        }
        else Assert.Equal("catalog_unavailable", result.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3651")]
    [InlineData("test-only-refresh-canary")]
    public void RefreshAfterDays_InvalidConfiguration_FailsSafelyAtStartup(string value)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:RefreshAfterDays"] = value })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("Catalog", error.Message);
        Assert.DoesNotContain("test-only-refresh-canary", error.ToString());
        Assert.Null(error.InnerException);
    }
}
