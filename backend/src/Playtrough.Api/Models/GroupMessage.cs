namespace Playtrough.Api.Models;

public sealed class GroupMessage
{
    public const int MaxTextLength = 2000;

    public long Id { get; set; }
    public int GroupId { get; set; }
    public int AuthorId { get; set; }
    public Guid ClientMessageId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public int? RemovedByUserId { get; set; }
}

public sealed class GroupMessageReceipt
{
    public long MessageId { get; set; }
    public int RecipientId { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
