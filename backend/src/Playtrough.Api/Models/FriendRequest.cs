namespace Playtrough.Api.Models;

public enum FriendRequestStatus { Pending, Accepted, Rejected, Canceled }

public sealed class FriendRequest
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public FriendRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public long? PendingPairKey { get; private set; }
}
