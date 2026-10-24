using Microsoft.Extensions.Options;
using Playtrough.Api.Models;

namespace Playtrough.Api.Catalog;

/// <summary>One shared, queued pace for initial requests and retries across all clients.</summary>
public sealed class RawgRequestLimiter(IOptions<RawgSettings> settings, TimeProvider time) : IDisposable
{
    private readonly SemaphoreSlim _queue = new(1, 1);
    // Round upwards so non-divisors such as 3 requests/s never shorten the interval.
    private readonly TimeSpan _interval = TimeSpan.FromTicks(
        (TimeSpan.TicksPerSecond + settings.Value.MaxRequestsPerSecond - 1L) / settings.Value.MaxRequestsPerSecond);
    private long? _lastGrant;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _queue.WaitAsync(cancellationToken);
        try
        {
            // Pace instead of allowing a bucket-sized burst on both sides of a window boundary.
            if (_lastGrant is { } last)
            {
                var remaining = _interval - time.GetElapsedTime(last);
                while (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, time, cancellationToken);
                    remaining = _interval - time.GetElapsedTime(last);
                }
            }
            _lastGrant = time.GetTimestamp();
        }
        finally { _queue.Release(); }
    }

    public void Dispose() => _queue.Dispose();
}
