namespace Playtrough.Api.Models;

public sealed class RoleChangeLog
{
    public int Id { get; set; }
    public int ChangedByUserId { get; set; }
    public int TargetUserId { get; set; }
    public string OldRole { get; set; } = string.Empty;
    public string NewRole { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
