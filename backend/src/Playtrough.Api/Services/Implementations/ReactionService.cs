using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

public interface IReactionTarget<TReaction> where TReaction : class, IContentReaction
{
    string ContentType { get; }
    Task<int> LockAndResolveOwnerAsync(long id, int actor, CancellationToken ct);
    IQueryable<TReaction> Reaction(long id, int actor);
    TReaction Create(long id, int actor, ReactionValue value, DateTime now);
    Task UpdateCountsAsync(long id, int likes, int dislikes, CancellationToken ct);
    Task<ReactionSummaryDto> ReadSummaryAsync(long id, int actor, CancellationToken ct);
}

public sealed class ReactionService(AppDbContext db, TimeProvider clock, IDomainEventPublisher events)
{
    public Task<ReactionSummaryDto> SetReactionAsync<TReaction>(int actor, long id,
        IReactionTarget<TReaction> target, SetReactionRequest request, CancellationToken ct)
        where TReaction : class, IContentReaction
        => ChangeAsync(actor, id, target, Parse(request.Value), ct);

    public Task<ReactionSummaryDto> RemoveReactionAsync<TReaction>(int actor, long id,
        IReactionTarget<TReaction> target, CancellationToken ct) where TReaction : class, IContentReaction
        => ChangeAsync(actor, id, target, null, ct);

    private async Task<ReactionSummaryDto> ChangeAsync<TReaction>(int actor, long id,
        IReactionTarget<TReaction> target, ReactionValue? value, CancellationToken ct)
        where TReaction : class, IContentReaction
    {
        var strategy = db.Database.CreateExecutionStrategy();
        (ReactionSummaryDto Summary, ContentReacted? Change) result;
        try
        {
            result = await strategy.ExecuteAsync(ApplyAsync, ct);
        }
        catch (DbUpdateException error) when (error.InnerException is MySqlException { Number: 1062 }
            && error.Entries.Count == 1 && error.Entries[0].Entity is TReaction)
        {
            // The losing insert rolled back. Reevaluate the transition against the winning row.
            result = await strategy.ExecuteAsync(ApplyAsync, ct);
        }

        if (result.Change is not null) await events.PublishAsync(result.Change, CancellationToken.None);
        return result.Summary;

        async Task<(ReactionSummaryDto, ContentReacted?)> ApplyAsync(CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var owner = await target.LockAndResolveOwnerAsync(id, actor, ct);
            if (value.HasValue && owner == actor) throw new TimelineException("cannot_react_to_own_content");

            var reaction = await target.Reaction(id, actor).AsTracking().SingleOrDefaultAsync(ct);
            var previous = reaction?.Value;
            try
            {
                ContentReacted? change = null;
                if (previous != value)
                {
                    if (value is null)
                        db.Remove(reaction!);
                    else if (reaction is null)
                    {
                        reaction = target.Create(id, actor, value.Value, clock.GetUtcNow().UtcDateTime);
                        db.Add(reaction);
                    }
                    else
                    {
                        reaction.Value = value.Value;
                        reaction.UpdatedAt = clock.GetUtcNow().UtcDateTime;
                    }

                    await target.UpdateCountsAsync(id, Delta(previous, value, ReactionValue.Like),
                        Delta(previous, value, ReactionValue.Dislike), ct);
                    await db.SaveChangesAsync(ct);
                    if (value == ReactionValue.Like && target is TimelineReactionTarget)
                        await OutboxEvents.SaveAsync(db, id, new TimelineEntryLiked(id, actor, owner),
                            clock.GetUtcNow().UtcDateTime, ct);
                    change = new(target.ContentType, id, actor, owner, Name(value), Name(previous));
                }
                var summary = await target.ReadSummaryAsync(id, actor, ct);
                await transaction.CommitAsync(ct);
                return (summary, change);
            }
            finally
            {
                // A retried transaction must load fresh state, even if SaveChanges had succeeded.
                if (reaction is not null) db.Entry(reaction).State = EntityState.Detached;
            }
        }
    }

    private static int Delta(ReactionValue? previous, ReactionValue? current, ReactionValue value)
        => (current == value ? 1 : 0) - (previous == value ? 1 : 0);

    internal static string? Name(ReactionValue? value) => value switch
    {
        ReactionValue.Like => "like",
        ReactionValue.Dislike => "dislike",
        _ => null,
    };

    private static ReactionValue Parse(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() switch
        {
            "like" => ReactionValue.Like,
            "dislike" => ReactionValue.Dislike,
            _ => throw new TimelineException("invalid_reaction"),
        }
        : throw new TimelineException("invalid_reaction");
}
