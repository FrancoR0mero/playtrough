using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

internal sealed class GamePersistenceTestContext : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string? _databasePath;
    public ServiceProvider Services { get; }
    public Mock<IGameCatalogProvider> Provider { get; } = new(MockBehavior.Strict);
    public CatalogPersistenceClock Clock { get; } = new();
    public GameInsertFailures InsertFailures { get; } = new();

    public GamePersistenceTestContext(bool concurrent = false, int refreshAfterDays = 30)
    {
        if (concurrent) _databasePath = Path.Combine(Path.GetTempPath(), $"playtrough-games-{Guid.NewGuid():N}.db");
        var connectionString = _databasePath is null ? "Data Source=:memory:" : new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Pooling = false, DefaultTimeout = 10,
        }.ToString();
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        if (concurrent)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteNonQuery();
        }
        Provider.Setup(provider => provider.GetByExternalIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => Details(id));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Provider.Object);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Options.Create(new CatalogSettings { RefreshAfterDays = refreshAfterDays }));
        services.AddDbContext<AppDbContext>(options =>
        {
            if (concurrent) options.UseSqlite(connectionString);
            else options.UseSqlite(_connection);
            options.AddInterceptors(InsertFailures);
        });
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IGameService, GameService>();
        Services = services.BuildServiceProvider();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public async Task<Game> GetAsync(int externalId = 42, string provider = "rawg", CancellationToken cancellationToken = default)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IGameService>().GetOrCreateAsync(provider, externalId, cancellationToken);
    }

    public async Task<Game> SeedAsync(TimeSpan age, int externalId = 42)
    {
        using var scope = Services.CreateScope();
        var game = LocalGame(Clock.GetUtcNow().UtcDateTime - age, externalId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    public async Task<List<Game>> ReadAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Games.AsNoTracking().OrderBy(game => game.Id).ToListAsync();
    }

    public static GameDetails Details(int id = 42) => new(id, "Example Game", "example-game", "https://images.example/small.jpg",
        new DateOnly(2020, 2, 29), ["PC", "Consola \"A\"", "Plataforma á & ñ"], "Resumen de ejemplo.",
        ["Aventura", "Acción"], ["Estudio ficticio"], "https://images.example/original.jpg");

    public static Game LocalGame(DateTime now, int id = 42) => new()
    {
        Provider = "rawg", ExternalId = id, Name = "Old name", Slug = "old-name", Summary = "Old summary",
        CoverImageUrl = null, FirstReleaseDate = null, Platforms = ["Old platform"], Genres = [], Developers = [],
        CreatedAt = now.AddDays(-1), UpdatedAt = now,
    };

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
        if (_databasePath is not null)
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_databasePath + suffix);
    }
}

internal sealed class CatalogPersistenceClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class GameInsertFailures : SaveChangesInterceptor
{
    private int _uniqueConflicts;
    public int UniqueConflicts => Volatile.Read(ref _uniqueConflicts);
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Exception is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 2067 } })
            Interlocked.Increment(ref _uniqueConflicts);
        return Task.CompletedTask;
    }
}
