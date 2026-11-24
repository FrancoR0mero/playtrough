using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Contracts;

internal static class TimelineValues
{
    public static string TypeValue(TimelineEntryType type) => type switch
    {
        TimelineEntryType.Added => "added",
        TimelineEntryType.StatusChanged => "status_changed",
        TimelineEntryType.Reviewed => "reviewed",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static TimelineEntryType ParseType(string type) => type switch
    {
        "added" => TimelineEntryType.Added,
        "status_changed" => TimelineEntryType.StatusChanged,
        "reviewed" => TimelineEntryType.Reviewed,
        _ => throw new InvalidOperationException("Invalid persisted timeline type."),
    };
}
