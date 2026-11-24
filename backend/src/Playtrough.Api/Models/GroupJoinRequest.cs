namespace Playtrough.Api.Models;

public enum GroupJoinRequestStatus { Pending, Approved, Rejected, Cancelled }

public sealed class GroupJoinRequest
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int UserId { get; set; }
    public GroupJoinRequestStatus Status { get; set; }
    public int? PendingUserId { get; private set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
