using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Playtrough.Api.Dtos.Notifications;

public sealed record NotificationItemDto(string Id, DateTime CreatedAt, DateTime? ReadAt, string Message, string? Destination);

public sealed record NotificationPageDto(IReadOnlyList<NotificationItemDto> Items, string? NextCursor, string? ReadCutoff);

public sealed record NotificationCountDto(int UnreadCount);

public sealed record ReadNotificationsRequest(string? Cutoff);

public sealed record NotificationPreferenceDto(string Category, bool Enabled, bool Editable);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateNotificationPreferenceRequest(
    [Required, MaxLength(32)] string Category,
    [Required] bool? Enabled);
