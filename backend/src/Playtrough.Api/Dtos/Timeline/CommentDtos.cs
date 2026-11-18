using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Api.Dtos.Timeline;

public sealed record CommentDto(long Id, FeedAuthorDto Author, string Text, DateTime CreatedAt, DateTime? EditedAt,
    int LikeCount, int DislikeCount, string? MyReaction, bool IsMine, bool CanDelete);
public sealed record CommentCreatedDto(CommentDto Comment, int CommentCount);
public sealed record CommentCountDto(int CommentCount);
public sealed record CommentPageDto(IReadOnlyList<CommentDto> Items, int CommentCount, string? NextCursor, bool HasMore);

public sealed class SaveCommentRequest
{
    public string? Text { get; init; }
}

public sealed class CommentListRequest
{
    [FromQuery(Name = "cursor"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Cursor { get; init; }
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
}
