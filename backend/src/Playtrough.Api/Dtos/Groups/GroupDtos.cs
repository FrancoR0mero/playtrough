namespace Playtrough.Api.Dtos.Groups;

public sealed record GroupDto(int Id, string Name, string Description, string Topic, string Privacy);
public sealed record GroupPageDto(IReadOnlyList<GroupDto> Items, int? NextCursor);

public sealed class CreateGroupRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Topic { get; init; }
    public string? Privacy { get; init; }
}
