namespace Playtrough.Api.Models;

public sealed class TimelineEntryComment
{
    public const int MaxTextLength = 500;

    public long Id { get; set; }
    public long EntryId { get; set; }
    public int AuthorId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }
}
