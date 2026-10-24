using System.Globalization;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Catalog;

public sealed class RawgGameCatalogProvider(RawgClient client, GameCatalogCache cache, IOptions<RawgSettings> settings)
    : IGameCatalogProvider
{
    public async Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        CatalogInput.ValidateSearch(query, page, pageSize);
        var normalized = query.Trim().ToLowerInvariant();
        return await cache.GetOrCreateAsync<GameSearchResult>($"catalog:rawg:search:{page}:{pageSize}:{normalized}",
            TimeSpan.FromMinutes(settings.Value.SearchCacheMinutes), async sharedToken =>
            {
                var response = await client.SearchAsync(normalized, page, pageSize, sharedToken);
                if (response?.Results is null) throw new GameCatalogUnavailableException();
                return new GameSearchResult(response.Results.Select(MapSummary).ToArray(), page, pageSize,
                    !string.IsNullOrWhiteSpace(response.Next));
            }, cancellationToken) ?? throw new GameCatalogUnavailableException();
    }

    public Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        return cache.GetOrCreateAsync<GameDetails>($"catalog:rawg:detail:{externalId}",
            TimeSpan.FromMinutes(settings.Value.DetailCacheMinutes), async sharedToken =>
            {
                var game = await client.GetByExternalIdAsync(externalId, sharedToken);
                if (game is null) return null;
                var summary = MapSummary(game);
                var text = game.DescriptionRaw ?? string.Empty;
                if (text.Length > 3000)
                {
                    var length = char.IsHighSurrogate(text[2999]) ? 2999 : 3000;
                    text = text[..length];
                }
                return new GameDetails(summary.ExternalId, summary.Name, summary.Slug, summary.CoverUrl,
                    summary.ReleaseDate, summary.Platforms, text, Names(game.Genres), Names(game.Developers), summary.CoverUrl);
            }, cancellationToken);
    }

    private static GameSummary MapSummary(RawgGameResponse? game)
    {
        if (game is null || game.Id <= 0 || string.IsNullOrWhiteSpace(game.Name) || string.IsNullOrWhiteSpace(game.Slug))
            throw new GameCatalogUnavailableException();
        DateOnly? released = DateOnly.TryParseExact(game.Released, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
        // background_image is usually landscape art/screenshot, not a portrait box cover.
        // No official resize/crop URL contract was found in RAWG's API documentation;
        // both cover sizes keep the original URL instead of relying on undocumented CDN paths.
        var cover = string.IsNullOrWhiteSpace(game.BackgroundImage) ? null : game.BackgroundImage;
        return new GameSummary(game.Id, game.Name, game.Slug, cover, released,
            Names(game.Platforms?.Select(item => item?.Platform)));
    }

    private static string[] Names(IEnumerable<RawgName?>? items)
        => items?.Select(item => item?.Name).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
}
