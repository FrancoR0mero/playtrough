using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playtrough.Api.Catalog;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

internal sealed class GamesApiTestContext : IDisposable
{
    private readonly LoginApiFactory _baseFactory;
    private int _users;
    public WebApplicationFactory<Program> Factory { get; }
    public RecordingGameCatalog Provider { get; } = new();

    public GamesApiTestContext(bool enableLimits = false, int? catalogLimit = 2, bool concurrent = false,
        string environment = "Testing")
    {
        _baseFactory = new LoginApiFactory(useIndependentConnections: concurrent, environment: environment);
        Factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGameCatalogProvider>();
            services.AddSingleton<IGameCatalogProvider>(Provider);
            services.PostConfigure<RateLimitSettings>(settings =>
            {
                settings.Enabled = enableLimits;
                if (catalogLimit is { } limit) settings.Catalog = new(limit, TimeSpan.FromMinutes(1));
                settings.Global = new(1000, TimeSpan.FromMinutes(1));
            });
            services.AddSingleton<IStartupFilter, GamesRemoteIpFilter>();
        }));
    }

    public async Task<HttpClient> AuthenticatedClientAsync(string role = "User")
    {
        var username = $"games_user_{++_users}";
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = username, Email = $"{username}@example.com", EmailConfirmed = true };
            var create = await users.CreateAsync(user, LoginApiFactory.Password);
            if (!create.Succeeded) throw new InvalidOperationException("Could not create catalog test user.");
            var addRole = await users.AddToRoleAsync(user, role);
            if (!addRole.Succeeded) throw new InvalidOperationException("Could not assign catalog test role.");
        }
        return await LoginClientAsync(username);
    }

    public async Task<HttpClient> LoginClientAsync(string username = "games_user_1")
    {
        var client = Factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = username, password = LoginApiFactory.Password,
        });
        login.EnsureSuccessStatusCode();
        var response = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        return client;
    }

    public async Task<List<Game>> ReadGamesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Games.AsNoTracking().ToListAsync();
    }

    public async Task MakeStaleAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Games.ExecuteUpdateAsync(update =>
            update.SetProperty(game => game.UpdatedAt, DateTime.UtcNow.AddDays(-31)));
    }

    public void Dispose() { Factory.Dispose(); _baseFactory.Dispose(); }
}

internal sealed class GamesRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(
                context.Request.Headers["X-Test-Remote-IP"].FirstOrDefault() ?? "203.0.113.10");
            return continuation(context);
        });
        next(app);
    };
}

internal sealed class RecordingGameCatalog : IGameCatalogProvider
{
    private readonly FakeGameCatalogProvider _fake = new();
    private int _searches;
    private int _details;
    public int SearchCalls => Volatile.Read(ref _searches);
    public int DetailCalls => Volatile.Read(ref _details);
    public bool Unavailable { get; set; }
    public bool Missing { get; set; }
    public GameSearchResult? SearchResult { get; set; }
    public (string Query, int Page, int PageSize)? LastSearch { get; private set; }

    public Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _searches);
        LastSearch = (query, page, pageSize);
        if (Unavailable) throw new GameCatalogUnavailableException();
        return SearchResult is not null ? Task.FromResult(SearchResult) : _fake.SearchAsync(query, page, pageSize, cancellationToken);
    }

    public Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _details);
        if (Unavailable) throw new GameCatalogUnavailableException();
        return Missing ? Task.FromResult<GameDetails?>(null) : _fake.GetByExternalIdAsync(externalId, cancellationToken);
    }
}
