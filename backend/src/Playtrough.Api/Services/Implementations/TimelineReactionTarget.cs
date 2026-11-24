using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

public sealed class TimelineReactionTarget(AppDbContext db, TimelineEntryAccess access) : IReactionTarget<TimelineEntryReaction>
{
    public string ContentType => "timeline_entry";

    public async Task<int> LockAndResolveOwnerAsync(long id, int actor, CancellationToken ct)
    {
        await access.LockAsync(id, ct);
        return await access.OwnerAsync(id, actor, ct)
            ?? throw new TimelineException("entry_not_found");
    }

    public IQueryable<TimelineEntryReaction> Reaction(long id, int actor)
        => db.TimelineEntryReactions.Where(reaction => reaction.EntryId == id && reaction.UserId == actor);

    public TimelineEntryReaction Create(long id, int actor, ReactionValue value, DateTime now)
        => new() { EntryId = id, UserId = actor, Value = value, CreatedAt = now, UpdatedAt = now };

    public Task UpdateCountsAsync(long id, int likes, int dislikes, CancellationToken ct)
        => db.TimelineEntries.Where(entry => entry.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(entry => entry.LikeCount, entry => entry.LikeCount + likes < 0 ? 0 : entry.LikeCount + likes)
            .SetProperty(entry => entry.DislikeCount, entry => entry.DislikeCount + dislikes < 0 ? 0 : entry.DislikeCount + dislikes), ct);

    public async Task<ReactionSummaryDto> ReadSummaryAsync(long id, int actor, CancellationToken ct)
    {
        var result = await db.TimelineEntries.Where(entry => entry.Id == id).Select(entry => new
        {
            entry.LikeCount,
            entry.DislikeCount,
            MyReaction = db.TimelineEntryReactions.Where(reaction => reaction.EntryId == entry.Id && reaction.UserId == actor)
                .Select(reaction => (ReactionValue?)reaction.Value).FirstOrDefault(),
        }).SingleAsync(ct);
        return new(ReactionService.Name(result.MyReaction), result.LikeCount, result.DislikeCount);
    }
}
