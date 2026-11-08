using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

internal sealed class FriendTestContext : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"playtrough-friends-{Guid.NewGuid():N}.db");
    private readonly string _connection;
    public CatalogPersistenceClock Clock { get; } = new() { UtcNow = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero) };
    public FriendsSettings Settings { get; } = new();
    public FriendSqlObserver Observer { get; } = new();
    public RecordingDomainEvents Events { get; } = new();

    public FriendTestContext()
    {
        _connection = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, DefaultTimeout = 20 }.ToString();
        using var db = CreateDb(false);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.EnsureCreated();
        db.Users.AddRange(Enumerable.Range(1, 60).Select(id => new ApplicationUser
        {
            Id = id, UserName = Name(id), NormalizedUserName = Name(id).ToUpperInvariant(),
            EmailConfirmed = true, CreatedAt = Clock.GetUtcNow().UtcDateTime,
        }));
        db.SaveChanges();
        Events.OnPublish = async domainEvent =>
        {
            // Independent connection proves publication happens after COMMIT.
            await using var check = CreateDb(false);
            if (domainEvent is FriendRequestSent sent)
                Assert.True(await check.FriendRequests.AnyAsync(r => r.Id == sent.RequestId));
            if (domainEvent is FriendRequestAccepted accepted)
            {
                Assert.True(await check.FriendRequests.AnyAsync(r => r.Id == accepted.RequestId && r.Status == FriendRequestStatus.Accepted));
                Assert.True(await check.Friendships.AnyAsync(f => f.UserLowId == Math.Min(accepted.SenderId, accepted.ReceiverId) &&
                    f.UserHighId == Math.Max(accepted.SenderId, accepted.ReceiverId)));
            }
        };
    }
    public static string Name(int id) => $"gamer_{id:D2}";
    public AppDbContext CreateDb(bool observe = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection);
        if (observe) options.AddInterceptors(Observer);
        return new(options.Options);
    }
    public FriendService Service(AppDbContext db) => new(db, new UpperInvariantLookupNormalizer(), Clock, Options.Create(Settings), Events);
    public async Task<T> Run<T>(Func<FriendService, Task<T>> action)
    {
        await using var db = CreateDb();
        return await action(Service(db));
    }
    public async Task Run(Func<FriendService, Task> action)
    {
        await using var db = CreateDb();
        await action(Service(db));
    }
    public void SynchronizeInitialReads()
    {
        var count = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Observer.BeforeUnlockedRead = async ct =>
        {
            var read = Interlocked.Increment(ref count);
            if (read == 2) ready.TrySetResult();
            if (read <= 2) await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        };
    }
    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
    }
}

internal sealed class RecordingDomainEvents : IDomainEventPublisher
{
    public ConcurrentQueue<IDomainEvent> Published { get; } = new();
    public Func<IDomainEvent, Task>? OnPublish { get; set; }
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        if (OnPublish is { } callback) await callback(domainEvent);
        Published.Enqueue(domainEvent);
    }
}

internal sealed class FriendSqlObserver : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();
    public Func<CancellationToken, Task>? BeforeUnlockedRead { get; set; }
    public bool FailFriendshipInsert { get; set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        if (FailFriendshipInsert && command.CommandText.StartsWith("INSERT INTO \"Friendships\""))
            throw new InvalidOperationException("Simulated friendship persistence failure");
        if (command.Transaction is null && command.CommandText.StartsWith("SELECT") && BeforeUnlockedRead is { } callback)
            await callback(cancellationToken);
        return result;
    }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        if (FailFriendshipInsert && command.CommandText.StartsWith("INSERT INTO \"Friendships\""))
            throw new InvalidOperationException("Simulated friendship persistence failure");
        return ValueTask.FromResult(result);
    }
}
