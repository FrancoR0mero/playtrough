using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class RawgDiagnosticTests
{
    [Fact]
    public async Task DnsFailure_LogsSafeCodesWithoutTheCredentialOrExceptionMessage()
    {
        using var context = new RawgTestContext((_, _, _) => throw new HttpRequestException(
            HttpRequestError.NameResolutionError, "https://api.rawg.io/api/games?key=" + RawgTestContext.Key,
            new SocketException((int)SocketError.HostNotFound)));
        var error = await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("portal", 1, 20));
        var log = Assert.Single(context.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("Cause: http_transport", log.Message);
        Assert.Contains("Stage: send", log.Message);
        Assert.Contains("HttpRequestError: NameResolutionError", log.Message);
        Assert.Contains("SocketError: HostNotFound", log.Message);
        Assert.DoesNotContain(RawgTestContext.Key, log.Message + log.State + error);
        Assert.DoesNotContain("?key=", log.Message + log.State);
        Assert.Null(log.Exception);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task InvalidJson_LogsTheReadStageAndSuccessfulHttpStatusWithoutResponseBody()
    {
        using var context = new RawgTestContext((_, _, _) => Task.FromResult(RawgTestContext.Json("<html>" + RawgTestContext.Key)));
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("portal", 1, 20));
        var log = Assert.Single(context.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("Cause: invalid_json", log.Message);
        Assert.Contains("Stage: read_json", log.Message);
        Assert.Contains("Status: 200", log.Message);
        Assert.DoesNotContain(RawgTestContext.Key, log.Message + log.State);
        Assert.Null(log.Exception);
    }

    [Fact]
    public async Task Timeout_LogsConfiguredLimitAndStage()
    {
        using var context = new RawgTestContext(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, settings => settings.RequestTimeoutSeconds = 1);
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.Provider.SearchAsync("portal", 1, 20));
        var log = Assert.Single(context.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("Cause: request_timeout", log.Message);
        Assert.Contains("Stage: send", log.Message);
        Assert.Contains("TimeoutSeconds: 1", log.Message);
        Assert.Null(log.Exception);
    }
}
