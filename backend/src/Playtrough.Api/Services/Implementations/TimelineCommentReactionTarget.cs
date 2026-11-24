using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

public sealed class TimelineCommentReactionTarget(AppDbContext db, TimelineEntryAccess access) : IReactionTarget<TimelineEntryCommentReaction>
{
    public string ContentType => "timeline_entry_comment";

    public async Task<int> LockAndResolveOwnerAsync(long id, int actor, CancellationToken ct)
    {
        var comments = db.TimelineEntryComments.Where(comment => comment.Id == id);
        // Lock the comment before reading its reaction. Entry and comment deletes
        // also have to acquire this row before they can cascade to its reactions.
        await comments.ExecuteUpdateAsync(setters => setters.SetProperty(comment => comment.LikeCount, comment => comment.LikeCount), ct);
        return await (from comment in comments
                      join entry in access.VisibleTo(actor) on comment.EntryId equals entry.Id
                      select (int?)comment.AuthorId).SingleOrDefaultAsync(ct)
            ?? throw new TimelineException("comment_not_found");
    }

    public IQueryable<TimelineEntryCommentReaction> Reaction(long id, int actor)
        => db.TimelineEntryCommentReactions.Where(reaction => reaction.CommentId == id && reaction.UserId == actor);

    public TimelineEntryCommentReaction Create(long id, int actor, ReactionValue value, DateTime now)
        => new() { CommentId = id, UserId = actor, Value = value, CreatedAt = now, UpdatedAt = now };

    public Task UpdateCountsAsync(long id, int likes, int dislikes, CancellationToken ct)
        => db.TimelineEntryComments.Where(comment => comment.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(comment => comment.LikeCount, comment => comment.LikeCount + likes < 0 ? 0 : comment.LikeCount + likes)
            .SetProperty(comment => comment.DislikeCount, comment => comment.DislikeCount + dislikes < 0 ? 0 : comment.DislikeCount + dislikes), ct);

    public async Task<ReactionSummaryDto> ReadSummaryAsync(long id, int actor, CancellationToken ct)
    {
        var result = await db.TimelineEntryComments.Where(comment => comment.Id == id).Select(comment => new
        {
            comment.LikeCount,
            comment.DislikeCount,
            MyReaction = db.TimelineEntryCommentReactions.Where(reaction => reaction.CommentId == comment.Id && reaction.UserId == actor)
                .Select(reaction => (ReactionValue?)reaction.Value).FirstOrDefault(),
        }).SingleAsync(ct);
        return new(ReactionService.Name(result.MyReaction), result.LikeCount, result.DislikeCount);
    }
}
