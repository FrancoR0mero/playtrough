using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Playtrough.Api.Data;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class ChatOutboxTests
{
    [Fact]
    public async Task HostedWorkerDispatchesAndAcknowledgesRegisteredEvents()
    {
        using var factory = new LoginApiFactory(useIndependentConnections: true);
        var handled = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler((message, _) =>
        {
            handled.TrySetResult(message.Id);
            return Task.CompletedTask;
        });
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOutboxHandler>();
            services.AddSingleton<IOutboxHandler>(handler);
        }));
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(), Type = Handler.MessageCreated, AggregateId = "1", Payload = "{}",
            OccurredAt = DateTime.UtcNow,
        };
        await SaveAsync(db, message);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        Assert.Equal(message.Id, await handled.Task.WaitAsync(deadline.Token));
        while ((await ReadAsync(db, message.Id)).ProcessedAt is null)
            await Task.Delay(25, deadline.Token);
        Assert.Equal(1, (await ReadAsync(db, message.Id)).Attempts);
    }

    [Fact]
    public async Task RetryIsDurableAndUnregisteredGroupEventsRemainPending()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb(false);
        var chat = Event(context);
        var group = Event(context, nameof(GroupJoinRequested));
        await SaveAsync(db, chat, group);
        var handler = new Handler((message, _) => message.Attempts == 1
            ? Task.FromException(new InvalidOperationException("temporarily unavailable")) : Task.CompletedTask);

        await Processor(db, context, handler).ProcessBatchAsync(default);
        var failedOnce = await ReadAsync(db, chat.Id);
        Assert.Equal(1, failedOnce.Attempts);
        Assert.Equal(context.Clock.UtcNow.UtcDateTime.AddSeconds(1), failedOnce.NextAttemptAt);
        Assert.Null(failedOnce.ProcessedAt);
        Assert.Null(failedOnce.LeaseId);
        await Processor(db, context, handler).ProcessBatchAsync(default);
        Assert.Equal(1, handler.Calls);

        context.Clock.UtcNow += TimeSpan.FromSeconds(1);
        await using var restarted = context.CreateDb(false);
        await Processor(restarted, context, handler).ProcessBatchAsync(default);
        await Processor(restarted, context, handler).ProcessBatchAsync(default);
        var delivered = await ReadAsync(restarted, chat.Id);
        Assert.Equal(2, delivered.Attempts);
        Assert.Equal(context.Clock.UtcNow.UtcDateTime, delivered.ProcessedAt);
        Assert.Null(delivered.NextAttemptAt);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(0, (await ReadAsync(restarted, group.Id)).Attempts);
    }

    [Fact]
    public async Task ExpiredLeaseCanBeRecoveredAndLateCompletionCannotOverrideTheNewWorker()
    {
        using var context = new ReviewTestContext();
        await using var firstDb = context.CreateDb(false);
        await using var secondDb = context.CreateDb(false);
        var item = Event(context);
        await SaveAsync(firstDb, item);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Handler(async (_, ct) =>
        {
            started.TrySetResult();
            await complete.Task.WaitAsync(ct);
        });
        var second = new Handler((_, _) => Task.FromException(new InvalidOperationException()));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var processing = Processor(firstDb, context, first).ProcessBatchAsync(deadline.Token);
        try
        {
            await started.Task.WaitAsync(deadline.Token);
            await Processor(secondDb, context, second).ProcessBatchAsync(deadline.Token);
            Assert.Equal(0, second.Calls);
            context.Clock.UtcNow += OutboxProcessor.LeaseDuration;
            await Processor(secondDb, context, second).ProcessBatchAsync(deadline.Token);
            Assert.Equal(1, second.Calls);
        }
        finally
        {
            complete.TrySetResult();
            await processing;
        }
        var row = await ReadAsync(secondDb, item.Id);
        Assert.Equal(2, row.Attempts);
        Assert.Null(row.ProcessedAt);
        Assert.Equal(context.Clock.UtcNow.UtcDateTime.AddSeconds(2), row.NextAttemptAt);
    }

    [Fact]
    public async Task ExhaustedAttemptsAreRetainedWithoutDispatchingAgain()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb(false);
        var exhausted = Event(context);
        exhausted.Attempts = OutboxProcessor.MaxAttempts - 1;
        var interrupted = Event(context);
        interrupted.Attempts = OutboxProcessor.MaxAttempts;
        interrupted.LeaseId = Guid.NewGuid();
        interrupted.LeaseExpiresAt = context.Clock.UtcNow.UtcDateTime.AddSeconds(-1);
        await SaveAsync(db, exhausted, interrupted);
        var handler = new Handler((_, _) => Task.FromException(new InvalidOperationException()));

        await Processor(db, context, handler).ProcessBatchAsync(default);
        await Processor(db, context, handler).ProcessBatchAsync(default);
        Assert.Equal(1, handler.Calls);
        var failed = await db.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.All(failed, row =>
        {
            Assert.Equal(OutboxProcessor.MaxAttempts, row.Attempts);
            Assert.NotNull(row.FailedAt);
            Assert.Null(row.ProcessedAt);
            Assert.Null(row.LeaseId);
        });
    }

    [Fact]
    public async Task MessageClientIdsAreUniquePerAuthor()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb(false);
        var author = context.UserIds[0];
        var now = context.Clock.UtcNow.UtcDateTime;
        var firstGroup = new SocialGroup { OwnerId = author, CreatedAt = now };
        var secondGroup = new SocialGroup { OwnerId = author, CreatedAt = now };
        db.SocialGroups.AddRange(firstGroup, secondGroup);
        await db.SaveChangesAsync();
        var clientId = Guid.NewGuid();
        db.GroupMessages.AddRange(Message(firstGroup.Id, author), Message(firstGroup.Id, context.UserIds[1]));
        await db.SaveChangesAsync();
        db.GroupMessages.Add(Message(secondGroup.Id, author));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(2, await db.GroupMessages.CountAsync());

        GroupMessage Message(int groupId, int authorId) => new()
        {
            GroupId = groupId, AuthorId = authorId, ClientMessageId = clientId, Text = "Hola 🎮", CreatedAt = now,
        };
    }

    private static OutboxProcessor Processor(AppDbContext db, ReviewTestContext context, Handler handler)
        => new(db, [handler], context.Clock, NullLogger<OutboxProcessor>.Instance);

    private static OutboxMessage Event(ReviewTestContext context, string type = Handler.MessageCreated) => new()
    {
        Id = Guid.NewGuid(), Type = type, AggregateId = "1", Payload = "{}", OccurredAt = context.Clock.UtcNow.UtcDateTime,
    };

    private static async Task SaveAsync(AppDbContext db, params OutboxMessage[] messages)
    {
        db.OutboxMessages.AddRange(messages);
        await db.SaveChangesAsync();
    }

    private static Task<OutboxMessage> ReadAsync(AppDbContext db, Guid id)
        => db.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id);

    private sealed class Handler(Func<OutboxMessage, CancellationToken, Task> dispatch) : IOutboxHandler
    {
        public const string MessageCreated = "ChatMessageCreated";
        public string EventType => MessageCreated;
        public int Calls { get; private set; }

        public Task HandleAsync(OutboxMessage message, CancellationToken ct)
        {
            Calls++;
            return dispatch(message, ct);
        }
    }
}
