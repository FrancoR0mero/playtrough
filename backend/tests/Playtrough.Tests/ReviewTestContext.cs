using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Playtrough.Api.Data;
using Playtrough.Api.Migrations;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Tests;

internal sealed class ReviewTestContext : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"playtrough-reviews-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    public CatalogPersistenceClock Clock { get; } = new()
    {
        UtcNow = new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero),
    };
    public ReviewSqlObserver Observer { get; } = new();
    public int GameId { get; }
    public int[] UserIds { get; }

    public ReviewTestContext()
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Pooling = false, DefaultTimeout = 10,
        }.ToString();
        using var db = CreateDb(observe: false);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.EnsureCreated();
        // EnsureCreated omits migration SQL. Install the migration's actual SQLite
        // trigger so cascade tests exercise the same lifecycle as a migrated database.
        var migration = new AddReviews { ActiveProvider = db.Database.ProviderName! };
        foreach (var operation in migration.UpOperations.OfType<SqlOperation>())
            db.Database.ExecuteSqlRaw(operation.Sql);
        var users = Enumerable.Range(1, 12).Select(index => new ApplicationUser
        {
            UserName = $"review-{index}", NormalizedUserName = $"REVIEW-{index}",
        }).ToArray();
        var game = GamePersistenceTestContext.LocalGame(Clock.GetUtcNow().UtcDateTime);
        db.Users.AddRange(users);
        db.Games.Add(game);
        db.SaveChanges();
        UserIds = users.Select(user => user.Id).ToArray();
        GameId = game.Id;
    }

    public AppDbContext CreateDb(bool observe = true, bool retry = false)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString);
        if (observe) options.AddInterceptors(Observer);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, ReviewTestExecutionStrategyFactory>();
        return new AppDbContext(options.Options);
    }

    public ReviewService Service(AppDbContext db) => new(db, Clock);

    public async Task<ReviewResult> UpsertAsync(decimal rating = 4.5m, string? text = null, int? userId = null, bool observe = true)
    {
        await using var db = CreateDb(observe);
        return await Service(db).UpsertAsync(userId ?? UserIds[0], GameId, rating, text);
    }

    public async Task<RatingSummary> DeleteAsync(int? userId = null, bool observe = true)
    {
        await using var db = CreateDb(observe);
        return await Service(db).DeleteAsync(userId ?? UserIds[0], GameId);
    }

    public async Task<ReviewResult> MineAsync(int? userId = null)
    {
        await using var db = CreateDb();
        return await Service(db).GetMineAsync(userId ?? UserIds[0], GameId);
    }

    public async Task<Game> GameAsync()
    {
        await using var db = CreateDb();
        return await db.Games.AsNoTracking().SingleAsync(game => game.Id == GameId);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_databasePath + suffix);
    }
}

internal sealed class ReviewTransientException : Exception;

internal sealed class ReviewTestExecutionStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new ReviewTestExecutionStrategy(dependencies);
}

internal sealed class ReviewTestExecutionStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, 2, TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => exception is ReviewTransientException;
}

internal sealed class ReviewSqlObserver : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();
    public Func<CancellationToken, Task>? AfterReviewRead { get; set; }
    public Func<CancellationToken, Task>? BeforeSummaryRead { get; set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        if (command.CommandText.StartsWith("SELECT \"g\".\"RatingCount\", \"g\".\"RatingSum\"") &&
            BeforeSummaryRead is { } callback)
            await callback(cancellationToken);
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("SELECT") && command.CommandText.Contains("FROM \"Reviews\"") && !command.CommandText.Contains("JOIN") &&
            AfterReviewRead is { } callback)
            await callback(cancellationToken);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
