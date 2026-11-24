using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

public sealed class TimelineCommentService(AppDbContext db, TimelineEntryAccess access, TimelineQuery timeline,
    TimelineCursorCodec cursors, TimeProvider clock, IDomainEventPublisher events)
{
    public async Task<CommentCreatedDto> AddAsync(int actor, long entryId, string? text, CancellationToken ct)
    {
        var (result, change) = await WriteAsync(entryId, async () =>
        {
            var owner = await access.OwnerAsync(entryId, actor, ct) ?? throw new TimelineException("entry_not_found");
            var clean = Normalize(text);
            var now = UtcNow();
            var cutoff = now.AddSeconds(-10);
            var recent = await db.TimelineEntryComments.Where(comment => comment.EntryId == entryId
                    && comment.AuthorId == actor && comment.CreatedAt > cutoff && comment.Text == clean)
                .Select(comment => comment.Text).ToListAsync(ct);
            if (recent.Contains(clean, StringComparer.Ordinal)) throw new TimelineException("duplicate_comment");

            var comment = new TimelineEntryComment { EntryId = entryId, AuthorId = actor, Text = clean, CreatedAt = now };
            db.Add(comment);
            try
            {
                var count = await UpdateCountAsync(entryId, 1, ct);
                await db.SaveChangesAsync(ct);
                var change = new TimelineEntryCommented(entryId, comment.Id, actor, owner, now);
                if (actor != owner) await OutboxEvents.SaveAsync(db, entryId, change, now, ct);
                var author = await db.Users.Where(user => user.Id == actor)
                    .Select(user => new { user.UserName, user.AvatarFileName }).SingleAsync(ct);
                return (new CommentCreatedDto(ToDto(comment, author.UserName!, author.AvatarFileName, actor, owner), count),
                    change);
            }
            finally
            {
                db.Entry(comment).State = EntityState.Detached;
            }
        }, ct);

        await events.PublishAsync(change, CancellationToken.None);
        return result;
    }

    public async Task<CommentPageDto> ListAsync(int viewer, long entryId, TimelineInteractionPageRequest request, CancellationToken ct)
    {
        var limit = timeline.Limit(request.Limit);
        var context = FormattableString.Invariant($"comments:{entryId}:{viewer}");
        var comments = db.TimelineEntryComments.AsNoTracking().Where(comment => comment.EntryId == entryId);
        if (request.Cursor is { } cursor)
        {
            var position = cursors.Decode(cursor, context);
            if (position.OccurredOn != DateOnly.MinValue) throw new TimelineException("invalid_cursor");
            comments = comments.Where(comment => comment.CreatedAt < position.RecordedAt
                || comment.CreatedAt == position.RecordedAt && comment.Id < position.Id);
        }
        var eligible = from comment in comments
                       join author in PrivacyService.UnblockedUsers(db, viewer) on comment.AuthorId equals author.Id
                       orderby comment.CreatedAt descending, comment.Id descending
                       select new { Comment = comment, author.UserName, author.AvatarFileName };
        var page = eligible.Take(limit + 1);
        var viewerReactions = db.TimelineEntryCommentReactions.AsNoTracking().Where(reaction => reaction.UserId == viewer);

        // The left join keeps the entry header even when the page has no comments.
        // Access, count and page all come from the same database snapshot.
        var rows = await (from entry in access.VisibleTo(viewer).Where(entry => entry.Id == entryId)
                          join item in page on entry.Id equals item.Comment.EntryId into pageRows
                          from item in pageRows.DefaultIfEmpty()
                          join reaction in viewerReactions on item.Comment.Id equals reaction.CommentId into reactions
                          from reaction in reactions.DefaultIfEmpty()
                          orderby item.Comment.CreatedAt descending, item.Comment.Id descending
                          select new { entry.CommentCount, Owner = entry.UserId, item.Comment, item.UserName, item.AvatarFileName,
                              MyReaction = (ReactionValue?)reaction.Value })
            .ToListAsync(ct);
        if (rows.Count == 0) throw new TimelineException("entry_not_found");
        var hasMore = rows.Count > limit;
        var items = rows.Where(row => row.Comment is not null).Take(limit)
            .Select(row => ToDto(row.Comment, row.UserName!, row.AvatarFileName, viewer, row.Owner, row.MyReaction)).ToArray();
        var next = hasMore ? cursors.Encode(new(DateOnly.MinValue, items[^1].CreatedAt, items[^1].Id), context) : null;
        return new(items, rows[0].CommentCount, next, hasMore);
    }

    public async Task<CommentDto> EditAsync(int actor, long commentId, string? text, CancellationToken ct)
    {
        var entryId = await ParentAsync(commentId, ct);
        return await WriteAsync(entryId, async () =>
        {
            var owner = await access.OwnerAsync(entryId, actor, ct) ?? throw new TimelineException("comment_not_found");
            var comments = db.TimelineEntryComments.Where(comment => comment.Id == commentId && comment.AuthorId == actor);
            var row = await (from comment in comments.AsNoTracking()
                             join author in db.Users.AsNoTracking() on comment.AuthorId equals author.Id
                             select new { Comment = comment, author.UserName, author.AvatarFileName }).SingleOrDefaultAsync(ct)
                ?? throw new TimelineException("comment_not_found");
            var clean = Normalize(text);
            if (!string.Equals(row.Comment.Text, clean, StringComparison.Ordinal))
            {
                var now = UtcNow();
                await comments.ExecuteUpdateAsync(setters => setters.SetProperty(comment => comment.Text, clean)
                    .SetProperty(comment => comment.EditedAt, now), ct);
                row.Comment.Text = clean;
                row.Comment.EditedAt = now;
            }
            return ToDto(row.Comment, row.UserName!, row.AvatarFileName, actor, owner);
        }, ct);
    }

    public async Task<CommentCountDto> DeleteAsync(int actor, long commentId, CancellationToken ct)
    {
        var entryId = await ParentAsync(commentId, ct);
        return await WriteAsync(entryId, async () =>
        {
            var owner = await access.OwnerAsync(entryId, actor, ct) ?? throw new TimelineException("comment_not_found");
            var deleted = await db.TimelineEntryComments.Where(comment => comment.Id == commentId
                && (comment.AuthorId == actor || owner == actor)).ExecuteDeleteAsync(ct);
            if (deleted == 0) throw new TimelineException("comment_not_found");
            return new CommentCountDto(await UpdateCountAsync(entryId, -1, ct));
        }, ct);
    }

    private Task<T> WriteAsync<T>(long entryId, Func<Task<T>> operation, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await access.LockAsync(entryId, token);
            var result = await operation();
            await transaction.CommitAsync(token);
            return result;
        }, ct);

    private async Task<long> ParentAsync(long commentId, CancellationToken ct)
        => await db.TimelineEntryComments.Where(comment => comment.Id == commentId).Select(comment => (long?)comment.EntryId)
            .SingleOrDefaultAsync(ct) ?? throw new TimelineException("comment_not_found");

    private async Task<int> UpdateCountAsync(long entryId, int delta, CancellationToken ct)
    {
        var entries = db.TimelineEntries.Where(entry => entry.Id == entryId);
        await entries.ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.CommentCount,
            entry => entry.CommentCount + delta < 0 ? 0 : entry.CommentCount + delta), ct);
        return await entries.Select(entry => entry.CommentCount).SingleAsync(ct);
    }

    private static CommentDto ToDto(TimelineEntryComment comment, string username, string? avatarFile, int viewer, int owner,
        ReactionValue? myReaction = null)
        => new(comment.Id, new(username, AvatarUrls.FromFile(avatarFile)), comment.Text, comment.CreatedAt, comment.EditedAt,
            comment.LikeCount, comment.DislikeCount, ReactionService.Name(myReaction), comment.AuthorId == viewer,
            comment.AuthorId == viewer || owner == viewer);

    private DateTime UtcNow()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }

    private static string Normalize(string? text)
    {
        var clean = PlainText.Sanitize(text);
        if (clean is null || clean.EnumerateRunes().Count() > TimelineEntryComment.MaxTextLength)
            throw new TimelineException("invalid_comment");
        return clean;
    }
}
