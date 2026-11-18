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

public sealed class TimelineCommentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t \r\n \u0000")]
    public async Task EmptyNormalizedTextIsRejected(string? text)
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        await TimelineReadTests.Error("invalid_comment", () => Service(context, db).AddAsync(context.Owner, entry.Id, text, default));
        Assert.Equal(0, await AssertCount(db, entry.Id));
    }

    [Fact]
    public async Task AddNormalizesTextCountsUnicodeAndPublishesAfterCommit()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        var changes = new List<TimelineEntryCommented>();
        var service = Service(context, db, new Recorder(async change =>
        {
            await using var reader = context.Store.CreateDb(false);
            Assert.Equal(change.CreatedAt, await reader.TimelineEntryComments.Where(row => row.Id == change.CommentId)
                .Select(row => row.CreatedAt).SingleAsync());
            Assert.Equal(entry.Id, change.EntryId);
            Assert.Equal(context.Owner, change.OwnerId);
            Assert.Equal(1 + changes.Count, await AssertCount(reader, entry.Id));
            changes.Add(change);
        }));
        var longText = string.Concat(Enumerable.Repeat("🎮", 500));
        await TimelineReadTests.Error("invalid_comment", () => service.AddAsync(context.Friend, entry.Id, longText + "x", default));
        var normalized = await service.AddAsync(context.Owner, entry.Id, "  texto\r\ncon\r\u0000saltos\t🎮  ", default);
        Assert.Equal("texto\ncon\nsaltos\t🎮", normalized.Comment.Text);
        Assert.True(normalized.Comment.IsMine);
        Assert.True(normalized.Comment.CanDelete);
        Assert.Equal(1, normalized.CommentCount);

        var boundary = await service.AddAsync(context.Friend, entry.Id, longText, default);
        Assert.Equal(longText, boundary.Comment.Text);
        Assert.Equal(2, boundary.CommentCount);
        Assert.Equal(context.Friend, changes[1].ActorId);
        Assert.Collection(changes, first => Assert.Equal(normalized.Comment.Id, first.CommentId),
            second => Assert.Equal(boundary.Comment.Id, second.CommentId));
    }

    [Fact]
    public async Task DuplicateIsScopedByEntryAuthorAndNormalizedTextForTenSeconds()
    {
        using var context = new TimelineReadTestContext();
        var first = await context.AddAsync();
        var second = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        var service = Service(context, db);
        await service.AddAsync(context.Friend, first.Id, "Uno\r\nDos", default);
        await service.AddAsync(context.Stranger, first.Id, "Uno\nDos", default);
        await service.AddAsync(context.Friend, second.Id, "Uno\nDos", default);
        await service.AddAsync(context.Friend, first.Id, "uno\nDos", default);

        context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddMilliseconds(9_999);
        await TimelineReadTests.Error("duplicate_comment", () => service.AddAsync(context.Friend, first.Id, " Uno\nDos ", default));
        Assert.Equal(3, await AssertCount(db, first.Id));
        context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddMilliseconds(1);
        Assert.Equal(4, (await service.AddAsync(context.Friend, first.Id, "Uno\nDos", default)).CommentCount);
        Assert.Equal(4, await AssertCount(db, first.Id));
    }

    [Fact]
    public async Task FailedInsertRollsBackCountAndPublishesNoEvent()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_comment BEFORE INSERT ON TimelineEntryComments
            BEGIN SELECT RAISE(ABORT, 'simulated insert failure'); END;
            """);
        var changes = new List<TimelineEntryCommented>();
        var service = Service(context, db, new Recorder(change => { changes.Add(change); return Task.CompletedTask; }));
        await Assert.ThrowsAsync<DbUpdateException>(() => service.AddAsync(context.Owner, entry.Id, "texto", default));
        Assert.Equal(0, await AssertCount(db, entry.Id));
        Assert.Empty(changes);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_comment;");
        Assert.Equal(1, (await service.AddAsync(context.Owner, entry.Id, "texto", default)).CommentCount);
        Assert.Single(changes);
    }

    [Fact]
    public async Task ConcurrentAddsAndDeletesKeepTheCountExactAndRejectDuplicates()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        var adds = await RaceAsync(Enumerable.Range(0, 9).Select(index =>
            new Func<AppDbContext, Task<long>>(async db => (await Service(context, db)
                .AddAsync(context.Friend, entry.Id, $"Comentario {index}", default)).Comment.Id)));
        var duplicateCodes = await RaceAsync(Enumerable.Range(0, 2).Select(_ =>
            new Func<AppDbContext, Task<string?>>(db => CodeAsync(
                () => Service(context, db).AddAsync(context.Friend, entry.Id, "duplicado", default)))));
        Assert.Single(duplicateCodes, code => code == "duplicate_comment");
        Assert.Single(duplicateCodes, code => code is null);
        await using var reader = context.Store.CreateDb(false);
        Assert.Equal(10, await AssertCount(reader, entry.Id));

        var deleteCodes = await RaceAsync(new[] { context.Owner, context.Friend }.Select(actor =>
            new Func<AppDbContext, Task<string?>>(db => CodeAsync(
                () => Service(context, db).DeleteAsync(actor, adds[0], default)))));
        Assert.Single(deleteCodes, code => code is null);
        Assert.Single(deleteCodes, code => code == "comment_not_found");
        Assert.Equal(9, await AssertCount(reader, entry.Id));

        async Task<T[]> RaceAsync<T>(IEnumerable<Func<AppDbContext, Task<T>>> actions)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes = actions.Select(action => Task.Run(async () =>
            {
                await start.Task;
                await using var db = context.Store.CreateDb(false);
                return await action(db);
            })).ToArray();
            start.SetResult();
            return await Task.WhenAll(writes);
        }
    }

    [Fact]
    public async Task ListKeepsKeysetOrderPermissionFlagsAndUsesOneQueryPerPage()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        var other = await context.AddAsync();
        await using var db = context.Store.CreateDb();
        var service = Service(context, db);
        context.Store.Observer.Commands.Clear();
        var empty = await service.ListAsync(context.Owner, entry.Id, new(), default);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.CommentCount);
        Assert.Single(context.Store.Observer.Commands);

        var first = (await service.AddAsync(context.Friend, entry.Id, "primero", default)).Comment;
        var second = (await service.AddAsync(context.Owner, entry.Id, "segundo", default)).Comment;
        context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddSeconds(1);
        var third = (await service.AddAsync(context.Friend, entry.Id, "tercero", default)).Comment;
        context.Store.Observer.Commands.Clear();
        var page = await service.ListAsync(context.Friend, entry.Id, new() { Limit = 2 }, default);
        Assert.Single(context.Store.Observer.Commands);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.CommentCount);
        Assert.Collection(page.Items,
            row =>
            {
                Assert.Equal(third.Id, row.Id);
                Assert.True(row.IsMine);
                Assert.True(row.CanDelete);
                Assert.Equal((0, 0, null), (row.LikeCount, row.DislikeCount, row.MyReaction));
            },
            row =>
            {
                Assert.Equal(second.Id, row.Id);
                Assert.False(row.IsMine);
                Assert.False(row.CanDelete);
            });
        await service.AddAsync(context.Friend, entry.Id, "nuevo", default);
        var last = await service.ListAsync(context.Friend, entry.Id, new() { Cursor = page.NextCursor }, default);
        Assert.Equal(first.Id, Assert.Single(last.Items).Id);
        Assert.Equal(4, last.CommentCount);
        Assert.Null(last.NextCursor);
        Assert.False(last.HasMore);
        Assert.All((await service.ListAsync(context.Owner, entry.Id, new(), default)).Items, row => Assert.True(row.CanDelete));
        await TimelineReadTests.Error("invalid_cursor", () => service.ListAsync(context.Friend, other.Id, new() { Cursor = page.NextCursor }, default));
        await TimelineReadTests.Error("invalid_cursor", () => service.ListAsync(context.Owner, entry.Id, new() { Cursor = page.NextCursor }, default));
    }

    [Fact]
    public async Task AuthorCanEditOnlyOnChangeAndOwnerCanDeleteEitherComment()
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        var service = Service(context, db);
        var first = (await service.AddAsync(context.Friend, entry.Id, "original", default)).Comment;
        var second = (await service.AddAsync(context.Friend, entry.Id, "otro", default)).Comment;
        await TimelineReadTests.Error("comment_not_found", () => service.EditAsync(context.Owner, first.Id, "texto", default));
        await TimelineReadTests.Error("comment_not_found", () => service.DeleteAsync(context.Stranger, first.Id, default));
        await TimelineReadTests.Error("invalid_comment", () => service.EditAsync(context.Friend, first.Id, " ", default));
        Assert.Null((await service.EditAsync(context.Friend, first.Id, " original ", default)).EditedAt);
        context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddSeconds(2);
        var updated = await service.EditAsync(context.Friend, first.Id, "editado", default);
        Assert.Equal("editado", updated.Text);
        Assert.Equal(first.CreatedAt, updated.CreatedAt);
        Assert.Equal(context.Store.Clock.UtcNow.UtcDateTime, updated.EditedAt);
        context.Store.Clock.UtcNow = context.Store.Clock.UtcNow.AddSeconds(2);
        Assert.Equal(updated, await service.EditAsync(context.Friend, first.Id, "editado", default));
        Assert.Equal(1, (await service.DeleteAsync(context.Friend, second.Id, default)).CommentCount);
        Assert.Equal(0, (await service.DeleteAsync(context.Owner, first.Id, default)).CommentCount);
        await TimelineReadTests.Error("comment_not_found", () => service.EditAsync(context.Friend, first.Id, "texto", default));
        Assert.Equal(0, await AssertCount(db, entry.Id));
    }

    [Theory]
    [InlineData(Visibility.Public, true)]
    [InlineData(Visibility.Friends, false)]
    [InlineData(Visibility.Private, false)]
    public async Task CommentAndDetailAccessTracksPrivacyHiddenFriendshipAndBlocks(Visibility visibility, bool publicAccess)
    {
        using var context = new TimelineReadTestContext();
        var entry = await context.AddAsync();
        await using var db = context.Store.CreateDb(false);
        await db.Users.Where(user => user.Id == context.Owner).ExecuteUpdateAsync(setters =>
            setters.SetProperty(user => user.ProfileVisibility, visibility));
        db.Friendships.Add(new() { UserLowId = context.Owner, UserHighId = context.Friend, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        var service = Service(context, db);
        var own = await service.AddAsync(context.Owner, entry.Id, "propio", default);
        var friend = await service.AddAsync(context.Friend, entry.Id, "amigo", default);
        if (publicAccess) Assert.Equal(2, (await service.ListAsync(context.Stranger, entry.Id, new(), default)).CommentCount);
        else await TimelineReadTests.Error("entry_not_found", () => service.ListAsync(context.Stranger, entry.Id, new(), default));
        await TimelineReadTests.Error("entry_not_found", () => service.AddAsync(context.Friend, long.MaxValue, "texto", default));
        await TimelineReadTests.Error("entry_not_found", () => context.Service(db).DetailAsync(context.Owner, long.MaxValue, default));

        await db.TimelineEntries.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsHidden, true));
        Assert.True((await context.Service(db).DetailAsync(context.Owner, entry.Id, default)).IsHidden);
        await TimelineReadTests.Error("entry_not_found", () => context.Service(db).DetailAsync(context.Friend, entry.Id, default));
        await TimelineReadTests.Error("entry_not_found", () => service.AddAsync(context.Friend, entry.Id, "nuevo", default));
        await TimelineReadTests.Error("comment_not_found", () => service.EditAsync(context.Friend, friend.Comment.Id, "editado", default));
        await TimelineReadTests.Error("comment_not_found", () => service.DeleteAsync(context.Friend, friend.Comment.Id, default));
        await db.TimelineEntries.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsHidden, false));
        await db.Friendships.ExecuteDeleteAsync();
        if (!publicAccess) await TimelineReadTests.Error("entry_not_found", () => service.ListAsync(context.Friend, entry.Id, new(), default));
        db.UserBlocks.Add(new() { BlockerId = context.Owner, BlockedId = context.Friend, CreatedAt = context.Store.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        await TimelineReadTests.Error("comment_not_found", () => service.DeleteAsync(context.Friend, friend.Comment.Id, default));
        Assert.Equal(2, await AssertCount(db, entry.Id));
        Assert.Equal(1, (await service.DeleteAsync(context.Owner, friend.Comment.Id, default)).CommentCount);
        Assert.Equal(own.Comment.Id, Assert.Single((await service.ListAsync(context.Owner, entry.Id, new(), default)).Items).Id);
        await db.TimelineEntries.ExecuteDeleteAsync();
        Assert.Empty(await db.TimelineEntryComments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void MySqlMigrationCreatesOnlyTheCommentTableWithUtf8AndEntryCascade()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261118061512_AddTimelineEntryReactions", "20261118063402_AddTimelineEntryComments");
        Assert.Contains("CREATE TABLE `TimelineEntryComments`", sql);
        Assert.Contains("`Text` varchar(500) CHARACTER SET utf8mb4 NOT NULL", sql);
        Assert.Contains("(`EntryId`, `CreatedAt`, `Id`)", sql);
        Assert.Contains("REFERENCES `TimelineEntries` (`Id`) ON DELETE CASCADE", sql);
        Assert.DoesNotContain("ALTER TABLE", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static TimelineCommentService Service(TimelineReadTestContext context, AppDbContext db, IDomainEventPublisher? events = null)
        => new(db, new(db), context.Query(db), context.Cursors, context.Store.Clock, events ?? new InMemoryDomainEventPublisher());

    private static async Task<int> AssertCount(AppDbContext db, long entryId)
    {
        var stored = await db.TimelineEntries.Where(entry => entry.Id == entryId).Select(entry => entry.CommentCount).SingleAsync();
        Assert.Equal(await db.TimelineEntryComments.CountAsync(comment => comment.EntryId == entryId), stored);
        return stored;
    }

    private static async Task<string?> CodeAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Playtrough.Api.Services.Contracts.TimelineException error)
        {
            return error.Code;
        }
    }

    private sealed class Recorder(Func<TimelineEntryCommented, Task> publish) : IDomainEventPublisher
    {
        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
            => publish(Assert.IsType<TimelineEntryCommented>(domainEvent));
    }
}
