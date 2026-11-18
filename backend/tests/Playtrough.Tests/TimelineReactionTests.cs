using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public sealed class TimelineReactionTests
{
    [Fact]
    public async Task Transitions_AreIdempotentAndPublishOnlyAfterCommit()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        var publisher = new Recorder(async change =>
        {
            Assert.Equal(new { ContentType = "timeline_entry", ContentId = entry.Id, ActorId = context.Friend, OwnerId = context.Owner },
                new { change.ContentType, change.ContentId, change.ActorId, change.OwnerId });
            await using var reader = context.Store.CreateDb(false);
            var value = await reader.TimelineEntryReactions.Select(row => (ReactionValue?)row.Value).SingleOrDefaultAsync();
            Assert.Equal(change.Value, ReactionService.Name(value));
        });
        await using var db = context.Store.CreateDb();
        var steps = new (string? Value, int Likes, int Dislikes, int Events)[]
        {
            (null, 0, 0, 0), ("like", 1, 0, 1), ("like", 1, 0, 1), ("dislike", 0, 1, 2),
            ("like", 1, 0, 3), (null, 0, 0, 4), ("dislike", 0, 1, 5), (null, 0, 0, 6),
        };
        foreach (var step in steps)
        {
            context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddSeconds(1);
            var summary = await Change(context, db, publisher, context.Friend, entry.Id, step.Value);
            Assert.Equal(new ReactionSummaryDto(step.Value, step.Likes, step.Dislikes), summary);
            Assert.Equal(step.Events, publisher.Changes.Count);
            await AssertCounts(db, entry.Id);
        }
        Assert.Equal(new string?[] { null, "like", "dislike", "like", null, "dislike" },
            publisher.Changes.Select(change => change.PreviousValue));
        Assert.Contains(context.Store.Observer.Commands, sql => sql.StartsWith("UPDATE") &&
            sql.Contains("\"LikeCount\" +") && sql.Contains("\"DislikeCount\" +"));
    }

    [Fact]
    public async Task ConcurrentWrites_FromSameAndDifferentActors_KeepOneRowPerUser()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        var publisher = new Recorder();
        var actors = context.Store.UserIds.Skip(1).Take(8).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = actors.Append(actors[0]).Append(actors[0]).Select((actor, i) => Task.Run(async () =>
        {
            await start.Task;
            await using var db = context.Store.CreateDb(false);
            return await Change(context, db, publisher, actor, entry.Id, i % 2 == 0 ? "like" : "dislike");
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(writes);
        await using var reader = context.Store.CreateDb(false);
        Assert.Equal(actors.Length, await reader.TimelineEntryReactions.CountAsync());
        await AssertCounts(reader, entry.Id);
    }

    [Fact]
    public async Task MixedSequence_MatchesRowsAndNeverProducesNegativeCounts()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        var publisher = new Recorder();
        var random = new Random(46);
        string?[] values = [null, "like", "dislike"];
        var actors = context.Store.UserIds.Skip(1).ToArray();
        for (var i = 0; i < 65; i++)
        {
            await Change(context, db, publisher, actors[random.Next(actors.Length)], entry.Id, values[random.Next(values.Length)]);
            await AssertCounts(db, entry.Id);
        }
    }

    [Fact]
    public async Task FailedInsert_RollsBackTheCounterAndDoesNotPublish()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_reaction BEFORE INSERT ON TimelineEntryReactions
            BEGIN SELECT RAISE(ABORT, 'simulated save failure'); END;
            """);
        var publisher = new Recorder();
        await Assert.ThrowsAsync<DbUpdateException>(() => Change(context, db, publisher, context.Friend, entry.Id, "like"));
        var stored = await db.TimelineEntries.AsNoTracking().SingleAsync();
        Assert.Equal(0, stored.LikeCount);
        Assert.Empty(await db.TimelineEntryReactions.ToListAsync());
        Assert.Empty(publisher.Changes);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_reaction;");
        Assert.Equal(1, (await Change(context, db, publisher, context.Friend, entry.Id, "like")).LikeCount);
        Assert.Single(publisher.Changes);
    }

    [Theory]
    [InlineData(Visibility.Public, true)]
    [InlineData(Visibility.Friends, false)]
    [InlineData(Visibility.Private, false)]
    public async Task AccessUsesCurrentPrivacyFriendshipHiddenAndBlock_WithoutDeletingReactions(Visibility visibility, bool strangersAllowed)
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        await db.Users.Where(user => user.Id == context.Owner).ExecuteUpdateAsync(setters =>
            setters.SetProperty(user => user.HistoryVisibility, visibility));
        db.Friendships.Add(new() { UserLowId = context.Owner, UserHighId = context.Friend, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        var publisher = new Recorder();
        Task<ReactionSummaryDto> Act(int actor, string? value = "like", long? id = null)
            => Change(context, db, publisher, actor, id ?? entry.Id, value);

        await TimelineReadTests.Error("cannot_react_to_own_content", () => Act(context.Owner));
        await TimelineReadTests.Error("entry_not_found", () => Act(context.Friend, id: long.MaxValue));
        Assert.Equal(1, (await Act(context.Friend)).LikeCount);
        if (strangersAllowed) Assert.Equal(2, (await Act(context.Stranger)).LikeCount);
        else await TimelineReadTests.Error("entry_not_found", () => Act(context.Stranger));

        await db.TimelineEntries.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsHidden, true));
        await TimelineReadTests.Error("entry_not_found", () => Act(context.Friend, null));
        await db.TimelineEntries.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsHidden, false));
        await db.Friendships.ExecuteDeleteAsync();
        if (!strangersAllowed) await TimelineReadTests.Error("entry_not_found", () => Act(context.Friend, null));
        await db.Users.Where(user => user.Id == context.Owner).ExecuteUpdateAsync(setters =>
            setters.SetProperty(user => user.HistoryVisibility, Visibility.Public));
        db.UserBlocks.Add(new() { BlockerId = context.Friend, BlockedId = context.Owner, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await TimelineReadTests.Error("entry_not_found", () => Act(context.Friend, null));
        await TimelineReadTests.Error("entry_not_found", () => Act(context.Friend, "dislike"));
        Assert.True(await db.TimelineEntryReactions.AnyAsync(row => row.UserId == context.Friend && row.Value == ReactionValue.Like));
        await AssertCounts(db, entry.Id);
    }

    [Fact]
    public async Task ReadsProjectOnlyTheViewersReactionInThePageQuery()
    {
        using var context = new TimelineReadTestContext();
        var first = await context.AddAsync();
        var second = await context.AddAsync();
        await using var db = context.Store.CreateDb();
        db.Friendships.Add(new() { UserLowId = context.Owner, UserHighId = context.Friend, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        var publisher = new Recorder();
        await Change(context, db, publisher, context.Friend, first.Id, "dislike");
        await Change(context, db, publisher, context.Stranger, first.Id, "like");
        await Change(context, db, publisher, context.Friend, second.Id, "like");
        var own = await context.ListAsync();
        Assert.All(own.Items, row => Assert.Null(row.MyReaction));
        var other = await context.ListAsync(viewer: context.Friend, username: "review-1");
        Assert.Equal("dislike", other.Items.Single(item => item.Id == first.Id).MyReaction);
        context.Store.Observer.Commands.Clear();
        var feed = await context.Query(db).FeedAsync(context.Friend, null, 20, default);
        Assert.Single(context.Store.Observer.Commands);
        Assert.Equal(2, feed.Count);
        var reacted = feed.Single(row => row.Id == first.Id);
        Assert.Equal((1, 1, 0, "dislike"), (reacted.LikeCount, reacted.DislikeCount, reacted.CommentCount, reacted.MyReaction));
    }

    [Fact]
    public void MySqlMigration_IsAdditiveAndIncludesCompositeKeyAndZeroDefaults()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261115031802_AddTimelineEntries");
        Assert.Contains("PRIMARY KEY (`EntryId`, `UserId`)", sql);
        Assert.Contains("(`EntryId`, `Value`, `CreatedAt`)", sql);
        Assert.Contains("ON DELETE CASCADE", sql);
        foreach (var field in new[] { "LikeCount", "DislikeCount", "CommentCount" })
            Assert.Contains($"ADD `{field}` int NOT NULL DEFAULT 0", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static Task<ReactionSummaryDto> Change(TimelineReadTestContext context, AppDbContext db,
        Recorder events, int actor, long id, string? value)
    {
        var service = new ReactionService(db, context.Store.Clock, events);
        var target = new TimelineReactionTarget(db, new TimelineEntryAccess(db));
        return value is null ? service.RemoveReactionAsync(actor, id, target, default)
            : service.SetReactionAsync(actor, id, target, new() { Value = JsonSerializer.SerializeToElement(value) }, default);
    }

    private static async Task AssertCounts(AppDbContext db, long id)
    {
        var row = await db.TimelineEntries.AsNoTracking().SingleAsync(entry => entry.Id == id);
        var reactions = await db.TimelineEntryReactions.Where(entry => entry.EntryId == id).ToListAsync();
        Assert.Equal(reactions.Count(entry => entry.Value == ReactionValue.Like), row.LikeCount);
        Assert.Equal(reactions.Count(entry => entry.Value == ReactionValue.Dislike), row.DislikeCount);
    }

    private sealed class Recorder(Func<ContentReacted, Task>? after = null) : IDomainEventPublisher
    {
        public ConcurrentQueue<ContentReacted> Changes { get; } = new();

        public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
        {
            var change = Assert.IsType<ContentReacted>(domainEvent);
            if (after is not null) await after(change);
            Changes.Enqueue(change);
        }
    }
}
