using System.Net;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class GameCatalogCacheTests
{
    private const string Search = """{"results":[],"next":null}""";
    private const string Game = """{"id":42,"name":"Example","slug":"example"}""";

    [Fact]
    public async Task Search_NormalizesQuery_SharesSnapshotAcrossPagesAndSizes_AndExpires()
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(Search)),
            settings => settings.SearchCacheMinutes = 2);
        await context.Provider.SearchAsync("  ExAmPlE  ", 1, 20);
        await context.Provider.SearchAsync("example", 1, 20);
        Assert.Equal(1, context.Handler.Count);
        await context.Provider.SearchAsync("example", 2, 20);
        await context.Provider.SearchAsync("example", 1, 40);
        Assert.Equal(1, context.Handler.Count);
        context.CacheClock.UtcNow += TimeSpan.FromMinutes(2) + TimeSpan.FromTicks(1);
        await context.Provider.SearchAsync("example", 1, 20);
        Assert.Equal(2, context.Handler.Count);
    }

    [Fact]
    public async Task Detail_IsCachedSeparatelyById_AndExpiresWithItsOwnDuration()
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json(Game)),
            settings => settings.DetailCacheMinutes = 60);
        await context.Provider.GetByExternalIdAsync(42);
        await context.Provider.GetByExternalIdAsync(42);
        Assert.Equal(1, context.Handler.Count);
        context.CacheClock.UtcNow += TimeSpan.FromMinutes(59);
        await context.Provider.GetByExternalIdAsync(42);
        Assert.Equal(1, context.Handler.Count);
        await context.Provider.GetByExternalIdAsync(43);
        Assert.Equal(2, context.Handler.Count);
        context.CacheClock.UtcNow += TimeSpan.FromMinutes(2);
        await context.Provider.GetByExternalIdAsync(42);
        Assert.Equal(3, context.Handler.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SimultaneousIdenticalCalls_AcrossProviderInstances_ShareOneRequest(bool detail)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = new RawgTestContext(async (_, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return RawgTestContext.Json(detail ? Game : Search);
        });
        Task Call() => detail ? context.Provider.GetByExternalIdAsync(42) : context.Provider.SearchAsync("example", 1, 20);
        var first = Call();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var rest = Enumerable.Range(0, 15).Select(_ => Call()).ToArray();
        Assert.Equal(1, context.Handler.Count);
        release.SetResult();
        await Task.WhenAll(rest.Append(first));
        Assert.Equal(1, context.Handler.Count);
    }

    [Fact]
    public async Task OneCancelledConsumer_DoesNotCancelSharedRequest()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = new RawgTestContext(async (_, _, _) =>
        {
            await release.Task;
            return RawgTestContext.Json(Search);
        });
        using var cancellation = new CancellationTokenSource();
        var cancelled = context.Provider.SearchAsync("example", 1, 20, cancellation.Token);
        var surviving = context.Provider.SearchAsync("example", 1, 20);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();
        Assert.IsType<GameSearchResult>(await surviving);
        Assert.Equal(1, context.Handler.Count);
    }

    [Fact]
    public async Task FailedSharedRequest_IsRemovedSoNextCallCanRecover()
    {
        using var context = new RawgTestContext((_, attempt, _) => Task.FromResult(attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : RawgTestContext.Json(Search)));
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("example", 1, 20));
        await context.Provider.SearchAsync("example", 1, 20);
        Assert.Equal(2, context.Handler.Count);
    }
}
