using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class FeedService(TimelineQuery query, TimelineCursorCodec cursors) : IFeedService
{
    public async Task<FeedPageDto> ListAsync(int viewerId, FeedListRequest request, CancellationToken ct = default)
    {
        var limit = query.Limit(request.Limit);
        var context = FormattableString.Invariant($"feed:{viewerId}");
        FeedPosition? position = null;
        if (request.Cursor is { } cursor)
        {
            var decoded = cursors.Decode(cursor, context);
            // Share the protected v1 envelope with timeline cursors. A feed has
            // no effective-date key, so its day field is always the fixed sentinel.
            if (decoded.OccurredOn != DateOnly.MinValue) throw new TimelineException("invalid_cursor");
            position = new(decoded.RecordedAt, decoded.Id);
        }
        var rows = await query.FeedAsync(viewerId, position, limit, ct);
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).ToArray();
        var last = items.LastOrDefault();
        return new(items, hasMore && last is not null
            ? cursors.Encode(new(DateOnly.MinValue, last.RecordedAt, last.Id), context) : null, hasMore);
    }
}
