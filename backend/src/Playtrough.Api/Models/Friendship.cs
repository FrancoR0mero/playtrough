namespace Playtrough.Api.Models;

public sealed class Friendship
{
    public int UserLowId { get; set; }
    public int UserHighId { get; set; }
    public DateTime CreatedAt { get; set; }
}
