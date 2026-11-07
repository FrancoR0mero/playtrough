using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Catalog;

public static class ConfigureGameCatalog
{
    public static IServiceCollection AddGameCatalog(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<GameBrainOptions>, GameBrainOptionsValidator>();
        services.AddOptions<GameBrainOptions>().BindSafely(configuration.GetSection(GameBrainOptions.SectionName))
            .Configure(settings => settings.ApiKey = ReadPrivateApiKey(configuration))
            .ValidateOnStart();
        services.AddOptions<CatalogSettings>().BindSafely(configuration.GetSection(CatalogSettings.SectionName))
            .Validate(settings => !settings.UseFake || environment.IsDevelopment(),
                "Catalog:UseFake can only be true in Development.")
            .Validate(settings => settings.RefreshAfterDays is >= 1 and <= 3650,
                "Catalog:RefreshAfterDays must be between 1 and 3650.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<GameBrainGate>();
        services.AddHostedService<GameMetadataCleanup>();
        services.AddSingleton<GameCatalogCache>();
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IGameService, GameService>();
        services.AddHttpClient<GameBrainClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<GameBrainOptions>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            // Each attempt includes body reading; queue/backoff are separate.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Do not forward the credential to a redirect target.
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        .RemoveAllLoggers();
        services.AddSingleton<DisabledGameCatalogProvider>();
        if (environment.IsDevelopment()) services.AddSingleton<FakeGameCatalogProvider>();
        services.AddTransient<IGameDataProvider>(provider =>
        {
            var catalog = provider.GetRequiredService<IOptions<CatalogSettings>>().Value;
            if (catalog.UseFake) return provider.GetRequiredService<FakeGameCatalogProvider>();
            return provider.GetRequiredService<IOptions<GameBrainOptions>>().Value.Enabled
                ? provider.GetRequiredService<GameBrainClient>()
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
                if (privateSource && provider.TryGet("GameBrain:ApiKey", out var key)) return key ?? string.Empty;
            }
        return string.Empty;
    }
}
