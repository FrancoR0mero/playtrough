using Playtrough.Api.Dtos.Timeline;

namespace Playtrough.Api.Services.Interfaces;

public interface IFeedService
{
    Task<FeedPageDto> ListAsync(int viewerId, FeedListRequest request, CancellationToken ct = default);
}
