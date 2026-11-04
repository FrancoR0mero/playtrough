using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Contracts;

/// <summary>Exact API/storage values; enum names and ordinals are not persisted.</summary>
public static class LibraryValues
{
    public static string StatusValue(LibraryStatus status) => status switch
    {
        LibraryStatus.Playing => "playing",
        LibraryStatus.Backlog => "backlog",
        LibraryStatus.Beaten => "beaten",
        LibraryStatus.Completed => "completed",
        LibraryStatus.Abandoned => "abandoned",
        _ => throw new LibraryException("invalid_status"),
    };

    public static LibraryStatus ParseStatus(string? status) => status switch
    {
        "playing" => LibraryStatus.Playing,
        "backlog" => LibraryStatus.Backlog,
        "beaten" => LibraryStatus.Beaten,
        "completed" => LibraryStatus.Completed,
        "abandoned" => LibraryStatus.Abandoned,
        _ => throw new LibraryException("invalid_status"),
    };

    public static string EventValue(LibraryEventType type) => type switch
    {
        LibraryEventType.Added => "added",
        LibraryEventType.StatusChanged => "status_changed",
        LibraryEventType.Removed => "removed",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static LibraryEventType ParseEvent(string type) => type switch
    {
        "added" => LibraryEventType.Added,
        "status_changed" => LibraryEventType.StatusChanged,
        "removed" => LibraryEventType.Removed,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static DateOnly EffectiveDate(DateOnly? occurredOn, DateTime utcNow, DateOnly? minDate = null)
    {
        var today = DateOnly.FromDateTime(utcNow);
        var date = occurredOn ?? today;
        if (date < new DateOnly(1970, 1, 1) || date > today.AddDays(1))
            throw new LibraryException("invalid_date");
        if (minDate is { } minimum && date < minimum)
            throw new LibraryException("date_before_previous_event", minimum);
        return date;
    }
}
