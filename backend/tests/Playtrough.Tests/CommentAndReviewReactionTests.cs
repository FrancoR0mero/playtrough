using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public sealed class CommentAndReviewReactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedTransitionsKeepExactCountsAndPublishOnlyEffectiveChanges(bool review)
    {
        using var context = new TimelineReadTestContext();
        var target = await CreateTargetAsync(context, review);
        await using var db = context.Store.CreateDb(false);
        var published = new List<ContentReacted>();
        var events = new Recorder(async change =>
        {
            await using var reader = context.Store.CreateDb(false);
            var stored = await target.Values(reader);
            var value = stored.SingleOrDefault(row => row.UserId == context.Friend)?.Value;
            Assert.Equal(change.Value, ReactionService.Name(value));
            Assert.Equal(target.Type, change.ContentType);
            Assert.Equal(target.Id, change.ContentId);
            Assert.Equal(context.Owner, change.OwnerId);
            Assert.Equal(context.Friend, change.ActorId);
            published.Add(change);
        });
        (string? Value, int Likes, int Dislikes, int Events)[] steps =
        [
            (null, 0, 0, 0), ("like", 1, 0, 1), ("like", 1, 0, 1), ("dislike", 0, 1, 2),
            ("dislike", 0, 1, 2), ("like", 1, 0, 3), (null, 0, 0, 4), ("dislike", 0, 1, 5), (null, 0, 0, 6),
        ];

        foreach (var step in steps)
        {
            Assert.Equal(new ReactionSummaryDto(step.Value, step.Likes, step.Dislikes),
                await target.Change(db, events, context.Friend, step.Value));
            Assert.Equal(step.Events, published.Count);
            await AssertCountsAsync(db, target, (step.Likes, step.Dislikes));
        }
        Assert.Equal(new string?[] { null, "like", "dislike", "like", null, "dislike" },
            published.Select(change => change.PreviousValue));
        await TimelineReadTests.Error("cannot_react_to_own_content",
            () => target.Change(db, events, context.Owner, "like"));
        Assert.Equal(6, published.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentWritesAreUniqueAndCounterDeltasMatchStoredRows(bool review)
    {
        using var context = new TimelineReadTestContext();
        var target = await CreateTargetAsync(context, review);
        var actors = context.Store.UserIds.Skip(1).Take(8).ToArray();
        var simultaneous = actors.Concat(actors.Take(3)).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = simultaneous.Select(actor => Task.Run(async () =>
        {
            await start.Task;
            await using var db = context.Store.CreateDb(false);
            await target.Change(db, new InMemoryDomainEventPublisher(), actor, actor % 2 == 0 ? "like" : "dislike");
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(writes);

        await using var reader = context.Store.CreateDb(false);
        var likes = actors.Count(actor => actor % 2 == 0);
        await AssertCountsAsync(reader, target, (likes, actors.Length - likes));
        Assert.Equal(actors.Order(), (await target.Values(reader)).Select(row => row.UserId).Order());
    }

    [Fact]
    public async Task CommentUsesParentAccessAndProjectsOnlyTheViewersValue()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb();
        var comments = new TimelineCommentService(db, new(db), context.Query(db), context.Cursors,
            context.Store.Clock, new InMemoryDomainEventPublisher());
        var comment = (await comments.AddAsync(context.Friend, entry.Id, "texto", default)).Comment;
        var target = new TimelineCommentReactionTarget(db, new(db));
        var reactions = new ReactionService(db, context.Store.Clock, new InMemoryDomainEventPublisher());
        await ChangeAsync(reactions, target, comment.Id, context.Stranger, "dislike");
        await ChangeAsync(reactions, target, comment.Id, context.Owner, "like");

        context.Store.Observer.Commands.Clear();
        var listed = Assert.Single((await comments.ListAsync(context.Stranger, entry.Id, new(), default)).Items);
        Assert.Single(context.Store.Observer.Commands);
        Assert.Equal((1, 1, "dislike"), (listed.LikeCount, listed.DislikeCount, listed.MyReaction));
        Assert.Equal("like", Assert.Single((await comments.ListAsync(context.Owner, entry.Id, new(), default)).Items).MyReaction);
        Assert.Null(Assert.Single((await comments.ListAsync(context.Friend, entry.Id, new(), default)).Items).MyReaction);

        await db.Users.Where(user => user.Id == context.Owner).ExecuteUpdateAsync(setters =>
            setters.SetProperty(user => user.HistoryVisibility, Visibility.Private));
        await Denied();
        db.Friendships.Add(new() { UserLowId = context.Owner, UserHighId = context.Stranger, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        Assert.Equal("like", (await ChangeAsync(reactions, target, comment.Id, context.Stranger, "like")).MyReaction);
        await db.TimelineEntries.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsHidden, true));
        await Denied();
        await db.TimelineEntries.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsHidden, false));
        db.UserBlocks.Add(new() { BlockerId = context.Stranger, BlockedId = context.Owner, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await Denied();
        Assert.Equal(2, await db.TimelineEntryCommentReactions.CountAsync());
        Assert.Equal(0, (await comments.DeleteAsync(context.Owner, comment.Id, default)).CommentCount);
        Assert.Empty(await db.TimelineEntryCommentReactions.ToListAsync());

        async Task Denied()
        {
            await TimelineReadTests.Error("comment_not_found", () => ChangeAsync(reactions, target, comment.Id, context.Stranger, "dislike"));
            await TimelineReadTests.Error("comment_not_found", () => ChangeAsync(reactions, target, comment.Id, context.Stranger, null));
        }
    }

    [Fact]
    public async Task ReviewReactionsRespectGameAndExistingReviewVisibilityAndCascadeOnDelete()
    {
        using var context = new TimelineReadTestContext();
        var review = (await context.Store.UpsertAsync()).Review;
        await using var db = context.Store.CreateDb(false);
        var target = new ReviewReactionTarget(db, review.GameId);
        var reactions = new ReactionService(db, context.Store.Clock, new InMemoryDomainEventPublisher());
        await ChangeAsync(reactions, target, review.Id, context.Stranger, "like");
        await TimelineReadTests.Error("review_not_found",
            () => ChangeAsync(reactions, new ReviewReactionTarget(db, review.GameId + 1), review.Id, context.Stranger, null));
        await TimelineReadTests.Error("review_not_found",
            () => ChangeAsync(reactions, target, int.MaxValue, context.Stranger, "like"));
        await db.Users.Where(user => user.Id == context.Owner).ExecuteUpdateAsync(setters =>
            setters.SetProperty(user => user.ProfileVisibility, Visibility.Private));
        await TimelineReadTests.Error("review_not_found",
            () => ChangeAsync(reactions, target, review.Id, context.Stranger, "dislike"));
        db.Friendships.Add(new() { UserLowId = context.Owner, UserHighId = context.Stranger, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        Assert.Equal("like", (await ChangeAsync(reactions, target, review.Id, context.Stranger, "like")).MyReaction);
        db.UserBlocks.Add(new() { BlockerId = context.Owner, BlockedId = context.Stranger, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await TimelineReadTests.Error("review_not_found",
            () => ChangeAsync(reactions, target, review.Id, context.Stranger, null));
        Assert.Single(await db.ReviewReactions.ToListAsync());
        await context.Store.DeleteAsync();
        Assert.Empty(await db.ReviewReactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HelpfulSortUsesScoreLikesDateAndIdAndPreservesTheRatingSummary()
    {
        using var context = new TimelineReadTestContext();
        var game = context.Store.GameId;
        var users = context.Store.UserIds;
        await using var db = context.Store.CreateDb();
        var service = context.Store.Service(db);
        async Task<int> Add(int user, decimal rating) => (await service.UpsertAsync(users[user], game, rating, null)).Review.Id;

        var moreLikes = await Add(0, 2m);
        var older = await Add(1, 3m);
        context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddSeconds(1);
        var newer = await Add(2, 4m);
        var higherId = await Add(3, 5m);
        var negative = await Add(4, 1m);
        var noReactions = await Add(5, 3.5m);
        var expected = new[] { moreLikes, higherId, newer, older, noReactions, negative };
        var summary = await service.GetSummaryAsync(game);
        var viewer = users[6];
        var target = new ReviewReactionTarget(db, game);
        var reactions = new ReactionService(db, context.Store.Clock, new InMemoryDomainEventPublisher());
        foreach (var id in new[] { moreLikes, older, newer, higherId }) await ChangeAsync(reactions, target, id, viewer, "like");
        await ChangeAsync(reactions, target, moreLikes, users[7], "like");
        await ChangeAsync(reactions, target, moreLikes, users[8], "dislike");
        await ChangeAsync(reactions, target, negative, viewer, "dislike");

        var actual = new List<ReviewListItemDto>();
        for (var page = 1; page <= 3; page++)
        {
            context.Store.Observer.Commands.Clear();
            var batch = await service.ListAsync(viewer, game, new ReviewListRequest { Sort = "helpful", Page = page, PageSize = 2 });
            Assert.Equal(2, context.Store.Observer.Commands.Count);
            Assert.Equal(summary, batch.RatingSummary);
            Assert.Equal(page < 3, batch.HasMore);
            actual.AddRange(batch.Items);
        }
        Assert.Equal(expected, actual.Select(row => row.Id));
        Assert.Equal((2, 1, "like"), (actual[0].LikeCount, actual[0].DislikeCount, actual[0].MyReaction));
        Assert.Null(actual[^2].MyReaction);
        Assert.Equal((0, 1, "dislike"), (actual[^1].LikeCount, actual[^1].DislikeCount, actual[^1].MyReaction));
        Assert.Null((await service.ListAsync(users[0], game, new ReviewListRequest { Sort = "helpful" })).Items[0].MyReaction);
        Assert.Equal(summary, await service.GetSummaryAsync(game));
    }

    [Fact]
    public void MySqlMigrationAddsTwoReactionTablesAndOnlyTheReviewCounts()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261118063402_AddTimelineEntryComments", "20261118064950_AddCommentAndReviewReactions");
        Assert.Contains("CREATE TABLE `ReviewReactions`", sql);
        Assert.Contains("CREATE TABLE `TimelineEntryCommentReactions`", sql);
        Assert.Contains("PRIMARY KEY (`ReviewId`, `UserId`)", sql);
        Assert.Contains("PRIMARY KEY (`CommentId`, `UserId`)", sql);
        Assert.Contains("REFERENCES `Reviews` (`Id`) ON DELETE CASCADE", sql);
        Assert.Contains("REFERENCES `TimelineEntryComments` (`Id`) ON DELETE CASCADE", sql);
        Assert.Contains("ALTER TABLE `Reviews` ADD `DislikeCount` int NOT NULL DEFAULT 0", sql);
        Assert.Contains("ALTER TABLE `Reviews` ADD `LikeCount` int NOT NULL DEFAULT 0", sql);
        Assert.DoesNotContain("ALTER TABLE `Games`", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static async Task<Target> CreateTargetAsync(TimelineReadTestContext context, bool review)
    {
        var clock = context.Store.Clock;
        if (review)
        {
            var row = (await context.Store.UpsertAsync()).Review;
            return new(row.Id, "review",
                (db, events, actor, value) => ChangeAsync(new ReactionService(db, clock, events),
                    new ReviewReactionTarget(db, row.GameId), row.Id, actor, value),
                db => db.Reviews.Where(review => review.Id == row.Id).Select(review => new Counts(review.LikeCount, review.DislikeCount)).SingleAsync(),
                db => db.ReviewReactions.Where(reaction => reaction.ReviewId == row.Id)
                    .Select(reaction => new StoredReaction(reaction.UserId, reaction.Value)).ToListAsync());
        }
        var entry = await context.AddAsync();
        await using var writer = context.Store.CreateDb(false);
        var comments = new TimelineCommentService(writer, new(writer), context.Query(writer), context.Cursors, clock, new InMemoryDomainEventPublisher());
        var comment = (await comments.AddAsync(context.Owner, entry.Id, "texto", default)).Comment;
        return new(comment.Id, "timeline_entry_comment",
            (db, events, actor, value) => ChangeAsync(new ReactionService(db, clock, events),
                new TimelineCommentReactionTarget(db, new(db)), comment.Id, actor, value),
            db => db.TimelineEntryComments.Where(row => row.Id == comment.Id).Select(row => new Counts(row.LikeCount, row.DislikeCount)).SingleAsync(),
            db => db.TimelineEntryCommentReactions.Where(reaction => reaction.CommentId == comment.Id)
                .Select(reaction => new StoredReaction(reaction.UserId, reaction.Value)).ToListAsync());
    }

    private static Task<ReactionSummaryDto> ChangeAsync<T>(ReactionService service, IReactionTarget<T> target, long id, int actor, string? value)
        where T : class, IContentReaction
        => value is null ? service.RemoveReactionAsync(actor, id, target, default)
            : service.SetReactionAsync(actor, id, target, new() { Value = JsonSerializer.SerializeToElement(value) }, default);

    private static async Task AssertCountsAsync(AppDbContext db, Target target, (int Likes, int Dislikes) expected)
    {
        var rows = await target.Values(db);
        Assert.Equal(expected, (rows.Count(row => row.Value == ReactionValue.Like), rows.Count(row => row.Value == ReactionValue.Dislike)));
        Assert.Equal(new Counts(expected.Likes, expected.Dislikes), await target.Counts(db));
    }

    private sealed record StoredReaction(int UserId, ReactionValue Value);
    private sealed record Counts(int Likes, int Dislikes);
    private sealed record Target(long Id, string Type, Func<AppDbContext, IDomainEventPublisher, int, string?, Task<ReactionSummaryDto>> Change,
        Func<AppDbContext, Task<Counts>> Counts, Func<AppDbContext, Task<List<StoredReaction>>> Values);
    private sealed class Recorder(Func<ContentReacted, Task> afterCommit) : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
            => afterCommit(Assert.IsType<ContentReacted>(domainEvent));
    }
}
