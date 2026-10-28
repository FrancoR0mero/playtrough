namespace Playtrough.Api.Models;

public enum LibraryEventType
{
    Added,
    StatusChanged,
    Removed,
}

public sealed class LibraryEvent
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GameId { get; set; }
    public LibraryEventType Type { get; set; }
    public LibraryStatus? FromStatus { get; set; }
    public LibraryStatus? ToStatus { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DateTime RecordedAt { get; set; }
}
