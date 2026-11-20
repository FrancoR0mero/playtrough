namespace Playtrough.Api.Dtos.Groups;

public sealed record ChatMessageDto(string Id, int GroupId, int AuthorId, string Username, string Text, DateTime CreatedAt);
