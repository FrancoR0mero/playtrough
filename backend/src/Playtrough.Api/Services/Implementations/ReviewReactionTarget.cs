using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

internal sealed class ReviewReactionTarget(AppDbContext db, int gameId) : IReactionTarget<ReviewReaction>
{
    public string ContentType => "review";

    public async Task<int> LockAndResolveOwnerAsync(long id, int actor, CancellationToken ct)
    {
        var reviews = ForGame(id);
        await reviews.ExecuteUpdateAsync(setters => setters.SetProperty(review => review.LikeCount, review => review.LikeCount), ct);
        return await (from review in reviews
                      join author in PrivacyService.VisibleUsers(db, actor) on review.UserId equals author.Id
                      select (int?)review.UserId).SingleOrDefaultAsync(ct)
            ?? throw new TimelineException("review_not_found");
    }

    public IQueryable<ReviewReaction> Reaction(long id, int actor)
        => db.ReviewReactions.Where(reaction => reaction.ReviewId == id && reaction.UserId == actor);

    public ReviewReaction Create(long id, int actor, ReactionValue value, DateTime now)
        => new() { ReviewId = checked((int)id), UserId = actor, Value = value, CreatedAt = now, UpdatedAt = now };

    public Task UpdateCountsAsync(long id, int likes, int dislikes, CancellationToken ct)
        => ForGame(id).ExecuteUpdateAsync(setters => setters
            .SetProperty(review => review.LikeCount, review => review.LikeCount + likes < 0 ? 0 : review.LikeCount + likes)
            .SetProperty(review => review.DislikeCount, review => review.DislikeCount + dislikes < 0 ? 0 : review.DislikeCount + dislikes), ct);

    public async Task<ReactionSummaryDto> ReadSummaryAsync(long id, int actor, CancellationToken ct)
    {
        var result = await ForGame(id).Select(review => new
        {
            review.LikeCount,
            review.DislikeCount,
            MyReaction = db.ReviewReactions.Where(reaction => reaction.ReviewId == review.Id && reaction.UserId == actor)
                .Select(reaction => (ReactionValue?)reaction.Value).FirstOrDefault(),
        }).SingleAsync(ct);
        return new(ReactionService.Name(result.MyReaction), result.LikeCount, result.DislikeCount);
    }

    private IQueryable<Review> ForGame(long id) => db.Reviews.Where(review => review.Id == id && review.GameId == gameId);
}
