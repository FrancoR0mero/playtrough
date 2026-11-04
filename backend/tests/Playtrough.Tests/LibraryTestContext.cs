using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Tests;

internal sealed class LibraryTestContext : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"playtrough-library-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    public CatalogPersistenceClock Clock { get; } = new()
    {
        UtcNow = new DateTimeOffset(2026, 10, 27, 14, 30, 0, TimeSpan.Zero),
    };
    public LibrarySaveObserver Observer { get; } = new();
    public int GameId { get; }
    public int UserId { get; }
    public int OtherUserId { get; }

    public LibraryTestContext()
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Pooling = false, DefaultTimeout = 10,
        }.ToString();
        using var db = CreateDb(observe: false);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.EnsureCreated();
        var user = new ApplicationUser { UserName = "library-a", NormalizedUserName = "LIBRARY-A" };
        var other = new ApplicationUser { UserName = "library-b", NormalizedUserName = "LIBRARY-B" };
        var game = GamePersistenceTestContext.LocalGame(Clock.GetUtcNow().UtcDateTime);
        db.Users.AddRange(user, other);
        db.Games.Add(game);
        db.SaveChanges();
        UserId = user.Id;
        OtherUserId = other.Id;
        GameId = game.Id;
    }

    public AppDbContext CreateDb(bool observe = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString);
        if (observe) options.AddInterceptors(Observer);
        return new AppDbContext(options.Options);
    }

    public LibraryService Service(AppDbContext db) => new(new LibraryRepository(db), Clock);

    public async Task<LibraryEntry> AddAsync(LibraryStatus status = LibraryStatus.Backlog, DateOnly? date = null,
        int? userId = null, bool observe = true)
    {
        await using var db = CreateDb(observe);
        return await Service(db).AddAsync(userId ?? UserId, GameId, status, date);
    }

    public async Task<LibraryEntry> ChangeAsync(LibraryStatus status, DateOnly? date = null, int? userId = null,
        bool observe = true)
    {
        await using var db = CreateDb(observe);
        return await Service(db).ChangeStatusAsync(userId ?? UserId, GameId, status, date);
    }

    public async Task RemoveAsync(int? userId = null, bool observe = true)
    {
        await using var db = CreateDb(observe);
        await Service(db).RemoveAsync(userId ?? UserId, GameId);
    }

    public async Task<List<LibraryEvent>> EventsAsync()
    {
        await using var db = CreateDb();
        return await db.LibraryEvents.AsNoTracking().OrderBy(change => change.Id).ToListAsync();
    }

    public async Task<LibraryEntry?> EntryAsync(int? userId = null)
    {
        await using var db = CreateDb();
        return await Service(db).GetEntryAsync(userId ?? UserId, GameId);
    }

    public async Task FailEventWritesAsync()
    {
        await using var db = CreateDb();
        // Fail inside the transaction, after any earlier INSERT/UPDATE/DELETE was executed.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_library_event BEFORE INSERT ON LibraryEvents
            BEGIN SELECT RAISE(ABORT, 'Simulated event persistence failure'); END;
            """);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_databasePath + suffix);
    }
}

internal sealed class LibrarySaveObserver : SaveChangesInterceptor
{
    private int _attempts;
    private int _uniqueConflicts;
    public int Attempts => Volatile.Read(ref _attempts);
    public int UniqueConflicts => Volatile.Read(ref _uniqueConflicts);
    public Func<DbContext, CancellationToken, Task>? BeforeSave { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var db = eventData.Context!;
        if (db.ChangeTracker.Entries<LibraryEntry>().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            Interlocked.Increment(ref _attempts);
            if (BeforeSave is { } callback) await callback(db, cancellationToken);
        }
        return result;
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Exception is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 2067 } })
            Interlocked.Increment(ref _uniqueConflicts);
        return Task.CompletedTask;
    }
}
