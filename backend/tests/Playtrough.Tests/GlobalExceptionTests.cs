using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Playtrough.Tests;

public class GlobalExceptionTests
{
    [Fact]
    public async Task Production_ReturnsGenericProblem_AndLogsSafeExceptionHierarchy()
    {
        using var factory = new LoginApiFactory(environment: "Production", includeSecurityEndpoints: true);
        using var logs = new CaptureLogs();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = configured.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__tests/security/throw");
        // A generic JSON error is still produced when the client requests HTML.
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Authorization = new("Bearer", SecurityTestController.SensitiveMarker);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        TransportEndpointsTests.AssertHeaders(response);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("internal_error", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(SecurityTestController.SensitiveMarker, body);
        Assert.DoesNotContain(nameof(InvalidOperationException), body);
        Assert.DoesNotContain(nameof(SecurityTestController), body);
        Assert.False(problem.RootElement.TryGetProperty("exception", out _));

        var error = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Unhandled API error."));
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Contains(nameof(InvalidOperationException), error.Message);
        Assert.Contains(nameof(IOException), error.Message);
        Assert.Contains(nameof(SecurityTestController), error.Message);
        Assert.Contains("ThrowInner", error.Message);
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain(SecurityTestController.SensitiveMarker, entry.Message);
            Assert.DoesNotContain(SecurityTestController.SensitiveMarker, entry.Exception?.ToString() ?? "");
        });
        Assert.Null(error.Exception);
    }

    [Fact]
    public async Task Development_KeepsDeveloperExceptionPage()
    {
        using var factory = new LoginApiFactory(environment: "Development", includeSecurityEndpoints: true);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__tests/security/throw");
        request.Headers.Accept.ParseAdd("text/plain");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(nameof(InvalidOperationException), await response.Content.ReadAsStringAsync());
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);
        public void Dispose() { }
        public record Entry(LogLevel Level, string Message, Exception? Exception);
        private sealed class CaptureLogger(ConcurrentQueue<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new(logLevel, formatter(state, exception), exception));
        }
    }
}
