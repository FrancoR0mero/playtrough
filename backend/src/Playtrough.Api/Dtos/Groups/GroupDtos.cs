namespace Playtrough.Api.Dtos.Groups;

public record GroupDto(int Id, string Name, string Description, string Topic, string Privacy);
public sealed record GroupPageDto(IReadOnlyList<GroupDto> Items, int? NextCursor);
public sealed record GroupProfileDto(int Id, string Name, string Description, string Topic, string Privacy,
    string Membership) : GroupDto(Id, Name, Description, Topic, Privacy);
public sealed record GroupMembershipDto(string Status);
public sealed record GroupPeoplePageDto<T>(IReadOnlyList<T> Items, int? NextCursor);
public sealed record GroupReviewPageDto(IReadOnlyList<GroupReviewDto> Items, int? NextCursor);
public sealed record GroupReviewDto(int Id, int GameId, string GameName, string Username,
    decimal Rating, string? Text, DateTime CreatedAt, DateTime? EditedAt);
public sealed record GroupMemberDto(int UserId, string Username, string? AvatarUrl, DateTime JoinedAt, bool IsOwner);
public sealed record GroupJoinRequestDto(int Id, int UserId, string Username, string? AvatarUrl, DateTime CreatedAt);
public sealed record GroupJoinDecisionRequest(string? Decision);

public sealed class SaveGroupRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Topic { get; init; }
    public string? Privacy { get; init; }
}
