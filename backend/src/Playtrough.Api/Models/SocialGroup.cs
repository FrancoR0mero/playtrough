namespace Playtrough.Api.Models;

public enum GroupPrivacy { Public, Private }

public sealed class SocialGroup
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 500;

    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GenreId { get; set; } = null!;
    public GroupGenre Genre { get; set; } = null!;
    public List<GroupSubgenreAssignment> Subgenres { get; } = [];
    public List<GroupTagAssignment> Tags { get; } = [];
    public GroupPrivacy Privacy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid ConcurrencyToken { get; set; }
}

public sealed class GroupMember
{
    public int GroupId { get; set; }
    public SocialGroup Group { get; set; } = null!;
    public int UserId { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
}
