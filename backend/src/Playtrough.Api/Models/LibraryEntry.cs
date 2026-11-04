namespace Playtrough.Api.Models;

public sealed class LibraryEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GameId { get; set; }
    public LibraryStatus Status { get; set; }
    public DateTime AddedAt { get; set; }
    public DateOnly StatusChangedOn { get; set; }
    public Guid ConcurrencyToken { get; set; }
}
