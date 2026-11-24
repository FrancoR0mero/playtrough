using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class TimelineSettings
{
    public const string SectionName = "Timeline";
    [Range(1, 50)] public int DefaultLimit { get; set; } = 20;
    [Range(1, 50)] public int MaxLimit { get; set; } = 50;
    [Range(1, 2000)] public int ExcerptLength { get; set; } = 240;
}

public sealed class FeedSettings
{
    public const string SectionName = "Feed";
    [Range(0, 36500)] public int MaxBackdateDays { get; set; } = 7;
}
