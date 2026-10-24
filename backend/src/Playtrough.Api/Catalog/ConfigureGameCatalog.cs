using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Catalog;

public static class ConfigureGameCatalog
{
    public static IServiceCollection AddGameCatalog(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<RawgSettings>, RawgSettingsValidator>();
        services.AddOptions<RawgSettings>().BindSafely(configuration.GetSection(RawgSettings.SectionName))
            .Configure(settings => settings.ApiKey = ReadPrivateApiKey(configuration))
            .ValidateOnStart();
        services.AddOptions<CatalogSettings>().BindSafely(configuration.GetSection(CatalogSettings.SectionName))
            .Validate(settings => !settings.UseFake || environment.IsDevelopment(),
                "Catalog:UseFake can only be true in Development.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<RawgRequestLimiter>();
        services.AddSingleton<GameCatalogCache>();
        services.AddHttpClient<RawgClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<RawgSettings>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            // RawgClient times each attempt including body reading; queue/backoff are separate.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Do not forward the query-string credential to a redirect target.
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        .RemoveAllLoggers();
        services.AddTransient<RawgGameCatalogProvider>();
        services.AddSingleton<DisabledGameCatalogProvider>();
        if (environment.IsDevelopment()) services.AddSingleton<FakeGameCatalogProvider>();
        services.AddTransient<IGameCatalogProvider>(provider =>
        {
            var catalog = provider.GetRequiredService<IOptions<CatalogSettings>>().Value;
            if (catalog.UseFake) return provider.GetRequiredService<FakeGameCatalogProvider>();
            return provider.GetRequiredService<IOptions<RawgSettings>>().Value.Enabled
                ? provider.GetRequiredService<RawgGameCatalogProvider>()
                : provider.GetRequiredService<DisabledGameCatalogProvider>();
        });
        return services;
    }

    internal static string ReadPrivateApiKey(IConfiguration configuration)
    {
        // Unlike ordinary options, the credential is accepted only from environment
        // providers or the standard user-secrets JSON provider, never appsettings/CLI.
        if (configuration is IConfigurationRoot root)
            foreach (var provider in root.Providers.Reverse())
            {
                var privateSource = provider is EnvironmentVariablesConfigurationProvider ||
                    provider is JsonConfigurationProvider json &&
                    string.Equals(Path.GetFileName(json.Source.Path), "secrets.json", StringComparison.OrdinalIgnoreCase);
                if (privateSource && provider.TryGet("RawgSettings:ApiKey", out var key)) return key ?? string.Empty;
            }
        return string.Empty;
    }
}
