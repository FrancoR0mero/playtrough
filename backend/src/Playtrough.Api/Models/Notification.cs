namespace Playtrough.Api.Models;

public sealed class Notification
{
    public long Id { get; set; }
    public int RecipientId { get; init; }
    public int? ActorId { get; init; }
    public required string Type { get; init; }
    public string? ResourceType { get; init; }
    public long? ResourceId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ReadAt { get; set; }
    public Guid EventId { get; init; }
    public byte PayloadVersion { get; init; }
}

public sealed class NotificationPreference
{
    public int UserId { get; init; }
    public required string Category { get; init; }
    public bool Enabled { get; set; }
}
