namespace Playtrough.Api.Models;

public sealed class Review
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GameId { get; set; }
    public decimal Rating { get; set; }
    public string? Text { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public Guid ConcurrencyToken { get; set; }
}
