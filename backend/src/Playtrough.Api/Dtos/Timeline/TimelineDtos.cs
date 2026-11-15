using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Api.Dtos.Timeline;

public sealed record TimelineGameDto(int GameId, int ExternalId, string Name, string Slug, string? CoverUrl, int? ReleaseYear);
public sealed record TimelineReviewDto(int Id, decimal Rating, string? Excerpt);
public sealed record TimelineEntryDto(long Id, string Type, DateOnly OccurredOn, DateTime RecordedAt,
    TimelineGameDto Game, string? FromStatus, string? ToStatus, TimelineReviewDto? Review, bool IsHidden);
public sealed record TimelinePageDto(IReadOnlyList<TimelineEntryDto> Items, string? NextCursor, bool HasMore);
public sealed record TimelineMonthDto(int Month, int Count);
public sealed record TimelineYearDto(int Year, int Count, IReadOnlyList<TimelineMonthDto> Months);
public sealed record TimelinePeriodsDto(IReadOnlyList<TimelineYearDto> Years);

public sealed class TimelineListRequest
{
    [FromQuery(Name = "cursor"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Cursor { get; init; }
    [FromQuery(Name = "limit")] public int? Limit { get; init; }
    [FromQuery(Name = "year")] public int? Year { get; init; }
    [FromQuery(Name = "month")] public int? Month { get; init; }
    [FromQuery(Name = "type"), DisplayFormat(ConvertEmptyStringToNull = false)] public string? Type { get; init; }
}
