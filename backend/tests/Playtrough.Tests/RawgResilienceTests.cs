using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Playtrough.Api.Catalog;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class RawgResilienceTests
{
    private const string Search = """{"results":[],"next":null}""";

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task TransientStatus_RetriesWithExponentialBackoff_ThenSucceeds(int status)
    {
        using var context = new RawgTestContext((_, attempt, _) => Task.FromResult(
            attempt <= 3 ? new HttpResponseMessage((HttpStatusCode)status) : RawgTestContext.Json(Search)));
        await context.Provider.SearchAsync("example", 1, 20);
        Assert.Equal(4, context.Handler.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)], context.Time.Delays);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfter_DeltaOrHttpDate_IsRespected(bool httpDate)
    {
        var origin = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        using var context = new RawgTestContext((_, attempt, _) =>
        {
            var response = attempt == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : RawgTestContext.Json(Search);
            if (attempt == 1) response.Headers.RetryAfter = httpDate
                ? new RetryConditionHeaderValue(origin.AddSeconds(7)) : new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });
        await context.Provider.SearchAsync("example", 1, 20);
        Assert.Equal(2, context.Handler.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(context.Time.Delays));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(302)]
    public async Task NonTransientStatus_DoesNotRetry(int status)
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("example", 1, 20));
        Assert.Equal(1, context.Handler.Count);
        Assert.Empty(context.Time.Delays);
        Assert.Contains(context.Logs.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains($"Status: {status}"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task RetryExhaustion_IsSafe_AndFailureIsNotCached(int status)
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        for (var call = 0; call < 2; call++)
        {
            var error = await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("example", 1, 20));
            Assert.Null(error.InnerException);
        }
        Assert.Equal(8, context.Handler.Count);
    }

    [Fact]
    public async Task ConfiguredTimeout_IsMappedToUnavailable()
    {
        using var context = new RawgTestContext(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }, settings => settings.RequestTimeoutSeconds = 1);
        var error = await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("example", 1, 20));
        Assert.Null(error.InnerException);
        Assert.Equal(1, context.Handler.Count);
    }

    [Fact]
    public async Task SharedLimiter_PacesConcurrentIndependentClients_AndRetries()
    {
        var timestamps = new System.Collections.Concurrent.ConcurrentQueue<long>();
        using var context = new RawgTestContext((_, attempt, _) =>
        {
            timestamps.Enqueue(Stopwatch.GetTimestamp());
            return Task.FromResult(attempt == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : RawgTestContext.Json(Search));
        }, settings => settings.MaxRequestsPerSecond = 2, TimeProvider.System);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(index => context.Provider.SearchAsync($"query {index}", 1, 20)));
        Assert.Equal(5, context.Handler.Count);
        var times = timestamps.ToArray();
        // Permit acquisition and handler entry differ slightly under scheduling; allow 20 ms.
        for (var index = 2; index < times.Length; index++)
            Assert.True(Stopwatch.GetElapsedTime(times[index - 2], times[index]) >= TimeSpan.FromMilliseconds(980));
    }

    [Fact]
    public async Task QueuedLimiter_CanBeCancelled()
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(Search)),
            settings => settings.MaxRequestsPerSecond = 1, TimeProvider.System);
        var limiter = context.Services.GetRequiredService<RawgRequestLimiter>();
        await limiter.WaitAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = limiter.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, context.Handler.Count);
    }

    [Fact]
    public async Task AllLogsAndExceptions_ExcludeKey_AcrossSuccessDetailFailureRetryAndTransportError()
    {
        using var context = new RawgTestContext((_, attempt, _) =>
        {
            if (attempt == 5) throw new HttpRequestException("https://api.rawg.io/api/games?key=" + RawgTestContext.Key,
                new IOException("sensitive " + RawgTestContext.Key));
            return Task.FromResult(attempt switch
            {
                1 or 4 => RawgTestContext.Json(Search),
                2 => RawgTestContext.Json("""{"id":42,"name":"Example","slug":"example"}"""),
                3 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    ReasonPhrase = "secret " + Uri.EscapeDataString(RawgTestContext.Key),
                    Content = new StringContent(RawgTestContext.Key),
                },
                _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            });
        });
        await context.Provider.SearchAsync("success", 1, 20);
        await context.Provider.GetByExternalIdAsync(42);
        await context.Provider.SearchAsync("retry", 1, 20);
        var transport = await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("transport", 1, 20));
        var unauthorized = await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("invalid key", 1, 20));
        Assert.NotEmpty(context.Logs.Entries);
        Assert.Contains(context.Logs.Entries, entry => entry.Message.StartsWith("RAWG retry."));
        Assert.DoesNotContain(context.Logs.Entries, entry => entry.Category.StartsWith("System.Net.Http.HttpClient"));
        foreach (var value in context.Logs.Entries.SelectMany(entry => new[] { entry.Message, entry.State, entry.Exception?.ToString() ?? "" })
            .Concat([transport.ToString(), unauthorized.ToString()]))
        {
            Assert.DoesNotContain(RawgTestContext.Key, value);
            Assert.DoesNotContain(Uri.EscapeDataString(RawgTestContext.Key), value);
            Assert.DoesNotContain("?key=", value);
        }
    }
}
