using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Catalog;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

internal sealed class RawgTestContext : IDisposable
{
    public const string Key = "test-rawg-secret-\"&injected=yes#á +";
    public ServiceProvider Services { get; }
    public CatalogHttpHandler Handler { get; }
    public CatalogLogs Logs { get; } = new();
    public TestCacheClock CacheClock { get; } = new();
    public AdvancingCatalogTime Time { get; } = new();
    public IGameCatalogProvider Provider => Services.GetRequiredService<IGameCatalogProvider>();

    public RawgTestContext(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond,
        Action<RawgSettings>? configure = null, TimeProvider? time = null)
    {
        Handler = new CatalogHttpHandler(respond);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns("Testing");
        services.AddGameCatalog(new ConfigurationBuilder().Build(), environment.Object);
        services.AddSingleton(time ?? Time);
        services.PostConfigure<RawgSettings>(settings =>
        {
            settings.Enabled = true;
            settings.ApiKey = Key;
            settings.MaxRequestsPerSecond = 1000;
            configure?.Invoke(settings);
        });
        services.Configure<MemoryCacheOptions>(options => options.Clock = CacheClock);
        services.AddHttpClient<RawgClient>().ConfigurePrimaryHttpMessageHandler(() => Handler);
        Services = services.BuildServiceProvider();
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    public void Dispose() { Services.Dispose(); Logs.Dispose(); }
}

internal sealed class CatalogHttpHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public ConcurrentQueue<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        return respond(request, Interlocked.Increment(ref _count), cancellationToken);
    }
}

internal sealed class CatalogLogs : ILoggerProvider
{
    public ConcurrentQueue<Entry> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries);
    public void Dispose() { }
    public record Entry(string Category, LogLevel Level, string Message, Exception? Exception, string State);
    private sealed class CaptureLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new(category, logLevel, formatter(state, exception), exception, state?.ToString() ?? ""));
    }
}

// Advances scheduled backoff/pacing without sleeping. The real limiter test uses system time.
internal sealed class AdvancingCatalogTime : TimeProvider
{
    private long _ticks;
    private readonly DateTimeOffset _origin = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    public ConcurrentQueue<TimeSpan> Delays { get; } = new();
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => _origin.AddTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Delays.Enqueue(dueTime);
        Interlocked.Add(ref _ticks, Math.Max(0, dueTime.Ticks));
        return new ImmediateTimer(callback, state);
    }

    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state)
            => ThreadPool.QueueUserWorkItem(_ => callback(state));
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
