using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Api.Dtos.Timeline;

public sealed record TimelineGameDto(int GameId, int ExternalId, string Name, string Slug, string? CoverUrl, int? ReleaseYear);
public sealed record TimelineReviewDto(int Id, decimal Rating, string? Excerpt);
public record TimelineEntryDto(long Id, string Type, DateOnly OccurredOn, DateTime RecordedAt,
    TimelineGameDto Game, string? FromStatus, string? ToStatus, TimelineReviewDto? Review, bool IsHidden)
{
    public int LikeCount { get; init; }
    public int DislikeCount { get; init; }
    public int CommentCount { get; init; }
    public string? MyReaction { get; init; }
}
public sealed record TimelinePageDto(IReadOnlyList<TimelineEntryDto> Items, string? NextCursor, bool HasMore);
public sealed record TimelineMonthDto(int Month, int Count);
public sealed record TimelineYearDto(int Year, int Count, IReadOnlyList<TimelineMonthDto> Months);
public sealed record TimelinePeriodsDto(IReadOnlyList<TimelineYearDto> Years);
public sealed record FeedAuthorDto(string Username, string? AvatarUrl);
public sealed record FeedEntryDto(long Id, string Type, DateOnly OccurredOn, DateTime RecordedAt,
    TimelineGameDto Game, string? FromStatus, string? ToStatus, TimelineReviewDto? Review, bool IsHidden,
    FeedAuthorDto Author) : TimelineEntryDto(Id, Type, OccurredOn, RecordedAt, Game, FromStatus, ToStatus, Review, IsHidden);
public sealed record FeedPageDto(IReadOnlyList<FeedEntryDto> Items, string? NextCursor, bool HasMore);
public sealed record TimelineLikeDto(FeedAuthorDto User, DateTime CreatedAt);
public sealed record TimelineLikesPageDto(IReadOnlyList<TimelineLikeDto> Items, string? NextCursor, bool HasMore);

public sealed class TimelineInteractionPageRequest
{
    [FromQuery(Name = "cursor"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Cursor { get; init; }
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
}

public sealed class FeedListRequest
{
    [FromQuery(Name = "cursor"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Cursor { get; init; }
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
}

public sealed class UpdateTimelineVisibilityRequest
{
    public JsonElement Hidden { get; init; }
}

public sealed class TimelineListRequest
{
    [FromQuery(Name = "cursor"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Cursor { get; init; }
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
    [FromQuery(Name = "year")] public int? Year { get; init; }
    [FromQuery(Name = "month")] public int? Month { get; init; }
    [FromQuery(Name = "type"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Type { get; init; }
}
