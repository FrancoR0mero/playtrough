namespace Playtrough.Api.Models;

public enum TimelineEntryType { Added, StatusChanged, Reviewed }

public sealed class TimelineEntry
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public TimelineEntryType Type { get; set; }
    public int GameId { get; set; }
    public LibraryStatus? FromStatus { get; set; }
    public LibraryStatus? ToStatus { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DateTime RecordedAt { get; set; }
    public int? LibraryEventId { get; set; }
    public LibraryEvent? LibraryEvent { get; set; }
    public int? ReviewId { get; set; }
    public Review? Review { get; set; }
    public bool IsHidden { get; set; }
    public bool FeedEligible { get; set; }
}
