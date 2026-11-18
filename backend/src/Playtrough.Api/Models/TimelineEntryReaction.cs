namespace Playtrough.Api.Models;

public enum ReactionValue { Like, Dislike }

public interface IContentReaction
{
    ReactionValue Value { get; set; }
    DateTime UpdatedAt { get; set; }
}

public sealed class TimelineEntryReaction : IContentReaction
{
    public long EntryId { get; set; }
    public int UserId { get; set; }
    public ReactionValue Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
