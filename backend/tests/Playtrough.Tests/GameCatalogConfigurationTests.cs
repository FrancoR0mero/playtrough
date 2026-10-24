using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playtrough.Api.Catalog;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class GameCatalogConfigurationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void EnabledWithoutKey_FailsStartup_WithoutExposingOtherSecrets(string environment)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RawgSettings>(settings =>
            {
                settings.Enabled = true;
                settings.ApiKey = "";
                settings.BaseUrl = "https://user:test-only-config-canary@invalid.example/?key=test-only-config-canary";
            })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("RawgSettings:ApiKey", error.Message);
        Assert.Contains("RawgSettings:BaseUrl", error.Message);
        Assert.DoesNotContain("test-only-config-canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("Enabled")]
    [InlineData("MaxRequestsPerSecond")]
    [InlineData("RequestTimeoutSeconds")]
    [InlineData("SearchCacheMinutes")]
    [InlineData("DetailCacheMinutes")]
    public void InvalidOptionType_DoesNotExposeBinderValue(string setting)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"RawgSettings:{setting}"] = "test-only-binding-canary",
            })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("RawgSettings", error.Message);
        Assert.DoesNotContain("test-only-binding-canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void InvalidFakeOptionType_IsSafe()
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:UseFake"] = "test-only-binding-canary" })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("Catalog", error.Message);
        Assert.DoesNotContain("test-only-binding-canary", error.ToString());
    }

    [Theory]
    [InlineData("MaxRequestsPerSecond")]
    [InlineData("RequestTimeoutSeconds")]
    [InlineData("SearchCacheMinutes")]
    [InlineData("DetailCacheMinutes")]
    public void NonPositiveOption_FailsStartupWithoutEchoingKey(string setting)
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RawgSettings>(settings =>
            {
                settings.ApiKey = "test-only-rawg-canary";
                typeof(RawgSettings).GetProperty(setting)!.SetValue(settings, 0);
            })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains($"RawgSettings:{setting}", error.Message);
        Assert.DoesNotContain("test-only-rawg-canary", error.ToString());
    }

    [Theory]
    [InlineData("Development", "search")]
    [InlineData("Development", "42")]
    [InlineData("Production", "search")]
    [InlineData("Production", "42")]
    public async Task DisabledWithoutKey_StartsAndReturnsGeneric503(string environment, string endpoint)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var logs = new CatalogLogs();
        using var configured = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureTestServices(AddProbeController));
        using var client = configured.CreateClient();
        Assert.IsType<DisabledGameCatalogProvider>(configured.Services.GetRequiredService<IGameCatalogProvider>());
        using var response = await client.GetAsync($"/__tests/catalog/{endpoint}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("catalog_unavailable", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(503, document.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("Intentá nuevamente", document.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(nameof(GameCatalogUnavailableException), body);
        Assert.All(logs.Entries, entry => Assert.Null(entry.Exception));
    }

    [Fact]
    public async Task FakeInDevelopment_ReturnsExamplesAndCoherentPaginationWithoutKey()
    {
        using var factory = new LoginApiFactory(environment: "Development");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            AddProbeController(services);
            services.PostConfigure<CatalogSettings>(settings => settings.UseFake = true);
        }));
        using var client = configured.CreateClient();
        Assert.IsType<FakeGameCatalogProvider>(configured.Services.GetRequiredService<IGameCatalogProvider>());
        using var response = await client.GetAsync("/__tests/catalog/search?q=WiTcHeR&page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, result.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(result.RootElement.GetProperty("hasMore").GetBoolean());
        using var last = await client.GetAsync("/__tests/catalog/search?q=witcher&page=2&pageSize=2");
        using var lastResult = JsonDocument.Parse(await last.Content.ReadAsStringAsync());
        Assert.Equal(1, lastResult.RootElement.GetProperty("items").GetArrayLength());
        Assert.False(lastResult.RootElement.GetProperty("hasMore").GetBoolean());
        using var detail = await client.GetAsync("/__tests/catalog/1");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var game = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, game.RootElement.GetProperty("coverUrl").ValueKind);
        Assert.Contains("ficticios", game.RootElement.GetProperty("summary").GetString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task RealProviderThroughApi_NeverExposesKey_InProblemsOrAnyLogs(string environment)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var logs = new CatalogLogs();
        var retries = 0;
        using var handler = new CatalogHttpHandler((request, _, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/42", StringComparison.Ordinal))
                return Task.FromResult(RawgTestContext.Json("""{"id":42,"name":"Example","slug":"example"}"""));
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query)["search"].ToString();
            if (query == "failure") throw new HttpRequestException("https://api.rawg.io/api/games?key=" + RawgTestContext.Key);
            if (query == "retry" && Interlocked.Increment(ref retries) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            return Task.FromResult(RawgTestContext.Json("""{"results":[],"next":null}"""));
        });
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            builder.ConfigureTestServices(services =>
            {
                AddProbeController(services);
                services.PostConfigure<RawgSettings>(settings =>
                {
                    settings.Enabled = true;
                    settings.ApiKey = RawgTestContext.Key;
                    settings.MaxRequestsPerSecond = 1000;
                });
                services.AddSingleton<TimeProvider>(new AdvancingCatalogTime());
                services.AddHttpClient<RawgClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });
        using var client = configured.CreateClient();
        using var search = await client.GetAsync("/__tests/catalog/search?q=success");
        using var detail = await client.GetAsync("/__tests/catalog/42");
        using var retry = await client.GetAsync("/__tests/catalog/search?q=retry");
        using var failure = await client.GetAsync("/__tests/catalog/search?q=failure");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(2, retries);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        var body = await failure.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("catalog_unavailable", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(nameof(HttpRequestException), body);
        Assert.Contains(logs.Entries, entry => entry.Message.StartsWith("RAWG retry."));
        foreach (var value in logs.Entries.SelectMany(entry => new[] { entry.Message, entry.State, entry.Exception?.ToString() ?? "" }).Append(body))
        {
            Assert.DoesNotContain(RawgTestContext.Key, value);
            Assert.DoesNotContain(Uri.EscapeDataString(RawgTestContext.Key), value);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void FakeOutsideDevelopment_RejectsStartup(string environment)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<CatalogSettings>(settings => settings.UseFake = true)));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("Catalog:UseFake", error.Message);
        Assert.Contains("Development", error.Message);
    }

    [Fact]
    public async Task Fake_HasTwentyExamples_HandlesMissingGamesAndPagesBeyondEnd()
    {
        var fake = new FakeGameCatalogProvider();
        var details = await Task.WhenAll(Enumerable.Range(1, 20).Select(id => fake.GetByExternalIdAsync(id)));
        Assert.All(details, game =>
        {
            Assert.NotNull(game);
            Assert.NotEmpty(game.Platforms);
            Assert.NotEmpty(game.Genres);
            Assert.NotEmpty(game.Developers);
            Assert.NotNull(game.ReleaseDate);
            Assert.Null(game.CoverUrl);
        });
        Assert.Null(await fake.GetByExternalIdAsync(999999));
        var beyond = await fake.SearchAsync("witcher", int.MaxValue, 40);
        Assert.Empty(beyond.Items);
        Assert.False(beyond.HasMore);
        var unknown = await fake.SearchAsync("does not exist", 1, 20);
        Assert.Empty(unknown.Items);
        Assert.False(unknown.HasMore);
    }

    [Fact]
    public void ApiKey_OnlyAcceptsEnvironmentOrUserSecrets_AndUsesTheirPrecedence()
    {
        var appsettings = new StoredJsonProvider("appsettings.json");
        appsettings.Set("RawgSettings:ApiKey", "test-only-file-key");
        var secret = new StoredJsonProvider("secrets.json");
        secret.Set("RawgSettings:ApiKey", "test-only-user-secret");
        var environment = new StoredEnvironmentProvider();
        environment.Set("RawgSettings:ApiKey", "test-only-environment-key");
        var ordinary = new ConfigurationBuilder().Add(new ExistingSource(appsettings))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RawgSettings:ApiKey"] = "test-only-cli-key" }).Build();
        Assert.Empty(ConfigureGameCatalog.ReadPrivateApiKey(ordinary));
        var secrets = new ConfigurationBuilder().Add(new ExistingSource(secret)).Build();
        Assert.Equal("test-only-user-secret", ConfigureGameCatalog.ReadPrivateApiKey(secrets));
        var combined = new ConfigurationBuilder().Add(new ExistingSource(secret))
            .Add(new ExistingSource(environment)).Add(new ExistingSource(appsettings)).Build();
        Assert.Equal("test-only-environment-key", ConfigureGameCatalog.ReadPrivateApiKey(combined));
    }

    [Fact]
    public void Defaults_AreDisabledAndConservative()
    {
        var settings = new RawgSettings();
        Assert.False(settings.Enabled);
        Assert.Empty(settings.ApiKey);
        Assert.Equal("https://api.rawg.io/api", settings.BaseUrl);
        Assert.Equal(5, settings.MaxRequestsPerSecond);
        Assert.Equal(10, settings.SearchCacheMinutes);
        Assert.Equal(1440, settings.DetailCacheMinutes);
        Assert.False(new CatalogSettings().UseFake);
        Assert.True(new RawgSettingsValidator().Validate(null, settings).Succeeded);
    }

    private sealed class ExistingSource(IConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private static void AddProbeController(IServiceCollection services)
        => services.AddControllers().ConfigureApplicationPartManager(manager =>
            manager.FeatureProviders.Add(new CatalogControllerFeatureProvider()));

    private sealed class StoredJsonProvider(string path) : JsonConfigurationProvider(new JsonConfigurationSource { Path = path })
    {
        public override void Load() { }
    }

    private sealed class StoredEnvironmentProvider : EnvironmentVariablesConfigurationProvider
    {
        public override void Load() { }
    }
}
