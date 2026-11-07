using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Playtrough.Api.Catalog;

namespace Playtrough.Api.Services.Implementations;

public sealed class GameService(IGameRepository repository, IGameDataProvider catalog,
    IOptions<CatalogSettings> settings, TimeProvider time, ILogger<GameService> logger) : IGameService
{
    public const string GameBrainProvider = "gamebrain";

    public async Task<Game> GetOrCreateAsync(string provider, int externalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        provider = provider.Trim().ToLowerInvariant();
        if (provider is not (GameBrainProvider or "fake")) throw new ArgumentException("Unsupported game provider.", nameof(provider));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        var existing = await repository.FindAsync(provider, externalId, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        if (existing is not null && GameMetadata.Expire(existing, now)) await repository.UpdateAsync(existing, cancellationToken);
        if (existing is not null && (provider == "fake"
            ? now - existing.UpdatedAt <= TimeSpan.FromDays(settings.Value.RefreshAfterDays)
            : existing.MetadataExpiresAt > now))
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
        catch (GameBrainQuotaException)
        {
            if (existing is not null) return existing;
            // Persist only an identifier, allowing library/reviews even when metadata is unavailable.
            return await repository.InsertOrGetExistingAsync(new Game { Provider = provider, ExternalId = externalId,
                GameBrainId = externalId, Name = $"Juego #{externalId}", CreatedAt = now, UpdatedAt = now }, cancellationToken);
        }

        var game = existing ?? new Game { Provider = provider, ExternalId = externalId, CreatedAt = now };
        if (provider == GameBrainProvider)
        {
            GameMetadata.PreserveLegacy(game);
            game.GameBrainId = externalId;
            game.SourceUrl = details.SourceUrl;
            game.MetadataExpiresAt = (details.MetadataFetchedAt ?? now).AddHours(24);
            game.ProviderDetailsJson = System.Text.Json.JsonSerializer.Serialize(details with { Offers = [] });
        }
        game.Name = details.Name;
        if (game.Provider != "rawg") game.Slug = details.Slug;
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
