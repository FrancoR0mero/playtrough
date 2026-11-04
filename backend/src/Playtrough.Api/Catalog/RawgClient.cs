using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Catalog;

public sealed class RawgClient(HttpClient http, IOptions<RawgSettings> settings,
    RawgRequestLimiter limiter, TimeProvider time, ILogger<RawgClient> logger)
{
    private const int MaxRetries = 3;

    internal Task<RawgSearchResponse?> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken)
        => GetAsync<RawgSearchResponse>("games", new Dictionary<string, string?>
        {
            ["search"] = query,
            ["search_precise"] = "true",
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
            // RAWG documents additions as DLC, GOTY/other editions and companion apps.
            ["exclude_additions"] = "true",
        }, allowNotFound: false, cancellationToken);

    internal Task<RawgGameResponse?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken)
        => GetAsync<RawgGameResponse>($"games/{externalId.ToString(CultureInfo.InvariantCulture)}",
            new Dictionary<string, string?>(), allowNotFound: true, cancellationToken);

    private async Task<T?> GetAsync<T>(string path, Dictionary<string, string?> query,
        bool allowNotFound, CancellationToken cancellationToken) where T : class
    {
        query["key"] = settings.Value.ApiKey;
        var uri = QueryHelpers.AddQueryString(path, query);
        var started = Stopwatch.GetTimestamp();
        var stage = "wait_limiter";
        int? responseStatus = null;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                stage = "wait_limiter";
                responseStatus = null;
                await limiter.WaitAsync(cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.RequestTimeoutSeconds));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                stage = "send";
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                responseStatus = (int)response.StatusCode;
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
                if (response.IsSuccessStatusCode)
                {
                    stage = "read_json";
                    return await response.Content.ReadFromJsonAsync<T>(timeout.Token)
                        ?? throw new GameCatalogUnavailableException();
                }

                var status = (int)response.StatusCode;
                if ((status == 429 || status >= 500 && status <= 599) && attempt < MaxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
                    var retryAfter = response.Headers.RetryAfter;
                    var providerDelay = retryAfter?.Delta ?? (retryAfter?.Date - time.GetUtcNow());
                    if (providerDelay > delay) delay = providerDelay.Value;
                    // Release the response/connection before waiting. No resilience package logs URLs.
                    response.Dispose();
                    logger.LogWarning("RAWG retry. Status: {StatusCode}. Retry: {Retry}.", status, attempt + 1);
                    stage = "backoff";
                    await Task.Delay(delay, time, cancellationToken);
                    continue;
                }
                // Never log the request URI, body, reason phrase or upstream exception.
                logger.LogError("RAWG request failed. Status: {StatusCode}.", status);
                throw new GameCatalogUnavailableException();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation must not carry a transport exception containing the URI.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException
            or OperationCanceledException or NotSupportedException)
        {
            // Log only types, enum codes and timings. Exception messages/objects may contain the key.
            var cause = error switch
            {
                OperationCanceledException => "request_timeout",
                JsonException => "invalid_json",
                HttpRequestException => "http_transport",
                IOException => "response_io",
                _ => "unsupported_response",
            };
            SocketError? socketError = null;
            for (Exception? current = error; current is not null; current = current.InnerException)
                if (current is SocketException socket) { socketError = socket.SocketErrorCode; break; }
            logger.LogError(
                "RAWG request failed. Cause: {Cause}. Stage: {Stage}. ExceptionType: {ExceptionType}. " +
                "InnerExceptionType: {InnerExceptionType}. HttpRequestError: {HttpRequestError}. " +
                "SocketError: {SocketError}. Status: {StatusCode}. ElapsedMs: {ElapsedMs}. TimeoutSeconds: {TimeoutSeconds}.",
                cause, stage, error.GetType().Name, error.InnerException?.GetType().Name,
                (error as HttpRequestException)?.HttpRequestError, socketError, responseStatus,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, settings.Value.RequestTimeoutSeconds);
            throw new GameCatalogUnavailableException();
        }
    }
}
