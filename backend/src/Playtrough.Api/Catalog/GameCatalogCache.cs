using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Playtrough.Api.Catalog;

public sealed class GameCatalogCache(IMemoryCache cache, IHostApplicationLifetime lifetime)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inflight = new();

    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan duration,
        Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cache.TryGetValue(key, out T? cached)) return cached;
        var flight = _inflight.GetOrAdd(key, ignoredKey => new Lazy<Task<object?>>(async () =>
        {
            try
            {
                if (cache.TryGetValue(key, out T? existing)) return existing;
                // Individual caller cancellation stops only that caller's wait, not shared work.
                var value = await factory(lifetime.ApplicationStopping);
                if (value is not null) cache.Set(key, value, duration);
                return value;
            }
            finally { _inflight.TryRemove(key, out _); }
        }));
        // Multiple application instances would require a distributed cache and coordination.
        return (T?)await flight.Value.WaitAsync(cancellationToken);
    }
}
