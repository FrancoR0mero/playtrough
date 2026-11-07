using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Catalog;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public sealed class GameBrainConfigurationTests
{
    [Fact]
    public void DisabledByDefault_AndNoKeyFromOrdinaryConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GameBrain:ApiKey"] = "must-not-be-used" }).Build();
        var environment = Mock.Of<IHostEnvironment>(value => value.EnvironmentName == "Production");
        var services = new ServiceCollection().AddLogging();
        services.AddGameCatalog(configuration, environment);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DisabledGameCatalogProvider>(provider.GetRequiredService<IGameDataProvider>());
        Assert.Empty(provider.GetRequiredService<IOptions<GameBrainOptions>>().Value.ApiKey);
    }

    [Theory]
    [InlineData("http://api.gamebrain.co/v1")]
    [InlineData("https://api.gamebrain.co/v1?api-key=secret")]
    [InlineData("https://user:secret@api.gamebrain.co/v1")]
    public void UnsafeBaseUrlsAreRejectedWithoutEchoingValues(string url)
    {
        var result = new GameBrainOptionsValidator().Validate(null, new() { BaseUrl = url });
        Assert.True(result.Failed);
        Assert.DoesNotContain("secret", result.FailureMessage);
    }

    [Fact]
    public void EnabledRequiresKey_AndAllCacheDurationsAreBounded()
    {
        var result = new GameBrainOptionsValidator().Validate(null, new() { Enabled = true, DetailCacheMinutes = 1441, SearchCacheMinutes = 1441, OffersCacheMinutes = 61 });
        Assert.True(result.Failed);
        Assert.Contains("GameBrain:ApiKey", result.FailureMessage);
        Assert.Contains("DetailCacheMinutes", result.FailureMessage);
        Assert.Contains("OffersCacheMinutes", result.FailureMessage);
    }
}
