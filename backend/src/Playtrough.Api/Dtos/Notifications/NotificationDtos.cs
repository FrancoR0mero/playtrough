namespace Playtrough.Api.Dtos.Notifications;

public sealed record NotificationItemDto(string Id, DateTime CreatedAt, DateTime? ReadAt, string Message, string? Destination);

public sealed record NotificationPageDto(IReadOnlyList<NotificationItemDto> Items, string? NextCursor, string? ReadCutoff);

public sealed record NotificationCountDto(int UnreadCount);

public sealed record ReadNotificationsRequest(string? Cutoff);
