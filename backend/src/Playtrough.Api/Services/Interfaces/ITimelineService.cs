using Playtrough.Api.Dtos.Timeline;

namespace Playtrough.Api.Services.Interfaces;

public interface ITimelineService
{
    Task<FeedEntryDto> DetailAsync(int viewerId, long id, CancellationToken ct = default);
    Task<TimelineLikesPageDto> LikesAsync(int viewerId, long id, TimelineInteractionPageRequest request, CancellationToken ct = default);
    Task<TimelinePageDto> ListAsync(int viewerId, string? username, TimelineListRequest request, CancellationToken ct = default);
    Task<TimelinePeriodsDto> PeriodsAsync(int viewerId, string? username, string? type, CancellationToken ct = default);
    Task<TimelineEntryDto> SetVisibilityAsync(int ownerId, long id, UpdateTimelineVisibilityRequest request, CancellationToken ct = default);
}
