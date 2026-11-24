namespace Playtrough.Api.Models;

public sealed class TimelineEntryCommentReaction : IContentReaction
{
    public long CommentId { get; set; }
    public int UserId { get; set; }
    public ReactionValue Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
