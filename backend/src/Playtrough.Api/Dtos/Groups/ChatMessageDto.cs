namespace Playtrough.Api.Dtos.Groups;

public sealed record ChatMessageDto(string Id, int GroupId, int AuthorId, string Username, string? Text, DateTime CreatedAt, DateTime? RemovedAt,
    bool Delivered = false, Guid? ClientMessageId = null);
public sealed record ChatHistoryPageDto(IReadOnlyList<ChatMessageDto> Items, string? NextCursor, string? Through);
public sealed record ChatMessageStateDto(string Id, DateTime? RemovedAt, bool Delivered = false);
public sealed record ChatParticipantDto(int UserId, string Username, bool IsTyping);
