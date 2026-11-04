using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Catalog;

public sealed class GameBrainQuotaException() : Exception("Game data quota exhausted.");
public sealed class GameBrainAuthenticationException() : Exception("Game data credentials rejected.");

public sealed class GameBrainClient(HttpClient http, IOptions<GameBrainOptions> options, GameBrainGate gate,
    GameCatalogCache cache, IMemoryCache memory, TimeProvider time, ILogger<GameBrainClient> logger) : IGameDataProvider
{
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public async Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        CatalogInput.ValidateSearch(query, page, pageSize);
        var offset = ((long)page - 1) * pageSize;
        if (offset > 1000) return new([], page, pageSize, false, true);
        try
        {
            var json = await CachedAsync($"games?query={Uri.EscapeDataString(query.Trim())}&offset={offset}&limit={pageSize}",
                options.Value.SearchCacheMinutes, 1440, cancellationToken);
            var items = GameBrainMapper.Results(json);
            var total = GameBrainMapper.Integer(GameBrainMapper.Field(json, "total_results")) ?? throw new GameCatalogUnavailableException();
            return new(items, page, pageSize, offset + pageSize < total && offset + pageSize <= 1000,
                offset + pageSize > 1000 && offset + items.Count < total);
        }
        catch (GameBrainQuotaException) { return new([], page, pageSize, false, true); }
    }

    public async Task<GameSearchResult> SuggestAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        CatalogInput.ValidateSearch(query, 1, limit);
        if (limit > 10) throw new ArgumentOutOfRangeException(nameof(limit));
        try
        {
            var json = await CachedAsync($"games/suggestions?query={Uri.EscapeDataString(query.Trim())}&limit={limit}",
                options.Value.SearchCacheMinutes, 1440, cancellationToken);
            return new(GameBrainMapper.Results(json), 1, limit, false);
        }
        catch (GameBrainQuotaException) { return new([], 1, limit, false, true); }
    }

    public async Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        var key = $"gamebrain:metadata:{externalId}";
        try
        {
            var json = await CachedAsync($"games/{Number(externalId)}", Math.Min(options.Value.DetailCacheMinutes, options.Value.OffersCacheMinutes),
                options.Value.OffersCacheMinutes, cancellationToken, true);
            if (json.ValueKind == JsonValueKind.Undefined) return null;
            var details = GameBrainMapper.Map(json) with { MetadataFetchedAt = memory.Get<DateTime?>("gamebrain:games/" + externalId + ":received") };
            // Only set on the first retrieval of this response, never extend an existing metadata fallback.
            var remaining = (details.MetadataFetchedAt ?? time.GetUtcNow().UtcDateTime)
                .AddMinutes(options.Value.DetailCacheMinutes) - time.GetUtcNow().UtcDateTime;
            if (remaining > TimeSpan.Zero && !memory.TryGetValue(key, out _)) memory.Set(key, details with { Offers = [] }, remaining);
            return details;
        }
        catch (GameBrainQuotaException) when (memory.TryGetValue<GameDetails>(key, out var fallback) && fallback is not null) { return fallback; }
    }

    public async Task<IReadOnlyList<GameSummary>> GetSimilarAsync(int externalId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        try { return GameBrainMapper.Results(await CachedAsync($"games/{Number(externalId)}/similar?limit={limit}", 60, 1440, cancellationToken)); }
        catch (GameBrainQuotaException) { return []; }
    }

    public async Task<int?> MatchAsync(GameMatchRequest request, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>();
        if (request.SteamId is > 0) query["steam_id"] = Number(request.SteamId.Value);
        else if (request.IgdbId is > 0) query["igdb_id"] = Number(request.IgdbId.Value);
        else if (!string.IsNullOrWhiteSpace(request.Name) && request.Year is >= 1 and <= 9999)
        { query["name"] = request.Name; query["year"] = Number(request.Year.Value); }
        else throw new ArgumentException("Matching requires Steam ID, IGDB ID, or name and year.");
        var json = await SendAsync(QueryHelpers.AddQueryString("games/match", query), true, cancellationToken);
        if (json.ValueKind == JsonValueKind.Undefined) return null;
        var id = GameBrainMapper.Integer(GameBrainMapper.Field(json, "id"));
        return id is > 0 ? id : throw new GameCatalogUnavailableException();
    }

    private sealed record CachedJson(JsonElement Value);
    private async Task<JsonElement> CachedAsync(string path, int freshMinutes, int fallbackMinutes, CancellationToken ct, bool missing = false)
    {
        var key = "gamebrain:" + path;
        try
        {
            var result = await cache.GetOrCreateAsync<CachedJson>(key, TimeSpan.FromMinutes(freshMinutes), async shared =>
            {
                try
                {
                    var value = await SendAsync(path, missing, shared);
                    if (value.ValueKind == JsonValueKind.Undefined) return null;
                    var entry = new CachedJson(value);
                    memory.Set(key + ":received", time.GetUtcNow().UtcDateTime, TimeSpan.FromHours(24));
                    memory.Set(key + ":fallback", entry, TimeSpan.FromMinutes(fallbackMinutes));
                    return entry;
                }
                catch (GameBrainQuotaException) when (memory.TryGetValue<CachedJson>(key + ":fallback", out var fallback) && fallback is not null)
                {
                    // Do not promote the fallback to fresh cache: that would extend its original lifetime.
                    throw new CachedFallbackException(fallback);
                }
            }, ct);
            return result?.Value ?? default;
        }
        catch (CachedFallbackException fallback) { return fallback.Entry.Value; }
    }
    private sealed class CachedFallbackException(CachedJson entry) : Exception { public CachedJson Entry { get; } = entry; }

    private async Task<JsonElement> SendAsync(string path, bool missing, CancellationToken ct)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                TimeSpan delay;
                await gate.EnterAsync(ct);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
                    using var request = new HttpRequestMessage(HttpMethod.Get, path);
                    request.Headers.Add("x-api-key", options.Value.ApiKey);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if (response.Headers.TryGetValues("X-API-Quota-Left", out var values) &&
                        decimal.TryParse(values.FirstOrDefault(), NumberStyles.Number, CultureInfo.InvariantCulture, out var quota))
                        logger.LogInformation("GameBrain quota remaining: {QuotaLeft}.", quota);
                    if (response.StatusCode == HttpStatusCode.PaymentRequired) throw new GameBrainQuotaException();
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new GameBrainAuthenticationException();
                    if (missing && response.StatusCode == HttpStatusCode.NotFound)
                    { logger.LogInformation("GameBrain returned no match (404)."); return default; }
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                        return document.RootElement.Clone();
                    }
                    if ((response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500) || attempt >= 3)
                        throw new GameCatalogUnavailableException();
                    delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
                    var retry = response.Headers.RetryAfter;
                    var advised = retry?.Delta ?? retry?.Date - time.GetUtcNow();
                    if (advised > delay) delay = advised.Value;
                    if (delay > TimeSpan.FromSeconds(30)) throw new GameCatalogUnavailableException();
                    logger.LogWarning("GameBrain retry. Status: {Status}. Retry: {Retry}.", (int)response.StatusCode, attempt + 1);
                }
                finally { gate.Exit(); }
                await Task.Delay(delay, time, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            logger.LogWarning("GameBrain transport failure: {Type}.", error.GetType().Name);
            throw new GameCatalogUnavailableException();
        }
    }
}
