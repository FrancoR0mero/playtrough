using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Stages entries only; the caller owns SaveChanges and the transaction.</summary>
public sealed class TimelineWriter(AppDbContext db, IOptions<FeedSettings> settings)
{
    public TimelineEntry? AddLibraryEvent(LibraryEvent change)
    {
        if (change.Type == LibraryEventType.Removed) return null;
        if (change.Type is not (LibraryEventType.Added or LibraryEventType.StatusChanged))
            throw new ArgumentOutOfRangeException(nameof(change));
        var entry = new TimelineEntry
        {
            UserId = change.UserId, GameId = change.GameId,
            Type = change.Type == LibraryEventType.Added ? TimelineEntryType.Added : TimelineEntryType.StatusChanged,
            FromStatus = change.FromStatus, ToStatus = change.ToStatus,
            OccurredOn = change.OccurredOn, RecordedAt = change.RecordedAt,
            LibraryEventId = change.Id == 0 ? null : change.Id,
            LibraryEvent = change.Id == 0 ? change : null,
            FeedEligible = change.OccurredOn.DayNumber >= DateOnly.FromDateTime(change.RecordedAt).DayNumber - settings.Value.MaxBackdateDays,
        };
        db.TimelineEntries.Add(entry);
        return entry;
    }

    public TimelineEntry AddReview(Review review)
    {
        var entry = new TimelineEntry
        {
            UserId = review.UserId, GameId = review.GameId, Type = TimelineEntryType.Reviewed,
            OccurredOn = DateOnly.FromDateTime(review.CreatedAt), RecordedAt = review.CreatedAt,
            ReviewId = review.Id == 0 ? null : review.Id, Review = review.Id == 0 ? review : null,
            FeedEligible = true,
        };
        db.TimelineEntries.Add(entry);
        return entry;
    }
}
