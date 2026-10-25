using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class GameService(IGameRepository repository, IGameCatalogProvider catalog,
    IOptions<CatalogSettings> settings, TimeProvider time, ILogger<GameService> logger) : IGameService
{
    public const string RawgProvider = "rawg";

    public async Task<Game> GetOrCreateAsync(string provider, int externalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        provider = provider.Trim().ToLowerInvariant();
        if (provider != RawgProvider) throw new ArgumentException("Only the rawg game provider is supported.", nameof(provider));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        var existing = await repository.FindAsync(provider, externalId, cancellationToken);
        if (existing is not null && time.GetUtcNow().UtcDateTime - existing.UpdatedAt <= TimeSpan.FromDays(settings.Value.RefreshAfterDays))
            return existing;

        GameDetails details;
        try
        {
            details = await catalog.GetByExternalIdAsync(externalId, cancellationToken) ?? throw new GameNotFoundException();
            if (details.ExternalId != externalId) throw new GameCatalogUnavailableException();
        }
        catch (GameCatalogUnavailableException) when (existing is not null)
        {
            // Do not advance UpdatedAt: the next use must still attempt a refresh.
            logger.LogWarning("Game refresh unavailable; returning local copy. GameId: {GameId}.", existing.Id);
            return existing;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var game = existing ?? new Game { Provider = provider, ExternalId = externalId, CreatedAt = now };
        game.Name = details.Name;
        game.Slug = details.Slug;
        game.Summary = details.Summary;
        // Phase 1 uses the same original image for both cover sizes. Store the original.
        game.CoverImageUrl = details.CoverUrlLarge ?? details.CoverUrl;
        game.FirstReleaseDate = details.ReleaseDate;
        game.Platforms = details.Platforms.ToList();
        game.Genres = details.Genres.ToList();
        game.Developers = details.Developers.ToList();
        game.UpdatedAt = now;

        if (existing is null) return await repository.InsertOrGetExistingAsync(game, cancellationToken);
        await repository.UpdateAsync(game, cancellationToken);
        return game;
    }
}
