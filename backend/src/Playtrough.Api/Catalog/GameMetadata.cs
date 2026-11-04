using System.Text.Json;
using Playtrough.Api.Models;

namespace Playtrough.Api.Catalog;

public static class GameMetadata
{
    private sealed record Legacy(string Name, string Slug, string Summary, string? CoverImageUrl,
        DateOnly? FirstReleaseDate, List<string> Platforms, List<string> Genres, List<string> Developers);

    public static void PreserveLegacy(Game game)
    {
        if (game.Provider != "rawg" || game.LegacyMetadataJson is not null) return;
        game.LegacyMetadataJson = JsonSerializer.Serialize(new Legacy(game.Name, game.Slug, game.Summary,
            game.CoverImageUrl, game.FirstReleaseDate, game.Platforms, game.Genres, game.Developers));
    }

    public static bool Expire(Game game, DateTime now)
    {
        if (game.MetadataExpiresAt is not { } expiry || expiry > now) return false;
        var old = game.LegacyMetadataJson is null ? null : JsonSerializer.Deserialize<Legacy>(game.LegacyMetadataJson);
        game.Name = old?.Name ?? $"Juego #{game.GameBrainId}";
        game.Slug = old?.Slug ?? string.Empty;
        game.Summary = old?.Summary ?? string.Empty;
        game.CoverImageUrl = old?.CoverImageUrl;
        game.FirstReleaseDate = old?.FirstReleaseDate;
        game.Platforms = old?.Platforms ?? [];
        game.Genres = old?.Genres ?? [];
        game.Developers = old?.Developers ?? [];
        game.MetadataExpiresAt = null;
        game.ProviderDetailsJson = null;
        // SourceUrl is a provider URL identifier and may be retained indefinitely.
        return true;
    }
}
