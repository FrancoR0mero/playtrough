namespace Playtrough.Api.Models;

public sealed record GroupGenre(string Id, string Name);
public sealed record GroupSubgenre(string Id, string GenreId, string Name, string? Alias = null);
public sealed record GroupTag(string Id, string Name, string Category);

public sealed class GroupSubgenreAssignment
{
    public int GroupId { get; set; }
    public string SubgenreId { get; set; } = null!;
}

public sealed class GroupTagAssignment
{
    public int GroupId { get; set; }
    public string TagId { get; set; } = null!;
}
