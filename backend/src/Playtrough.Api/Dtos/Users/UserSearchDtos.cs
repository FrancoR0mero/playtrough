namespace Playtrough.Api.Dtos.Users;

public sealed class UserSearchRequest
{
    public string? Q { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record UserSearchItemDto(string Username, string? AvatarUrl, RelationshipDto Relationship);
public sealed record UserSearchResponse(IReadOnlyList<UserSearchItemDto> Items, int Page, int PageSize, bool HasMore);
