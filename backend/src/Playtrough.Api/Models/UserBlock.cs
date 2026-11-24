namespace Playtrough.Api.Models;

public sealed class UserBlock
{
    public int BlockerId { get; set; }
    public int BlockedId { get; set; }
    public DateTime CreatedAt { get; set; }
}
