using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Catalog;

/// <summary>Development examples only. Dates, platforms, genres and studios are invented.</summary>
public sealed class FakeGameCatalogProvider : IGameCatalogProvider
{
    private static readonly IReadOnlyList<GameDetails> Games = CreateGames();

    public Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CatalogInput.ValidateSearch(query, page, pageSize);
        var matching = Games.Where(game => game.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var offset = ((long)page - 1) * pageSize;
        var items = offset >= matching.Length ? [] : matching.Skip((int)offset).Take(pageSize)
            .Select(game => new GameSummary(game.ExternalId, game.Name, game.Slug, game.CoverUrl, game.ReleaseDate, game.Platforms)).ToArray();
        return Task.FromResult(new GameSearchResult(items, page, pageSize, offset + items.Length < matching.Length));
    }

    public Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        return Task.FromResult(Games.FirstOrDefault(game => game.ExternalId == externalId));
    }

    private static IReadOnlyList<GameDetails> CreateGames()
    {
        string[] names = ["The Witcher 3", "The Witcher 2", "The Witcher", "Portal", "Portal 2",
            "Half-Life", "Half-Life 2", "Stardew Valley", "Hollow Knight", "Celeste", "Hades",
            "Minecraft", "Terraria", "Elden Ring", "Dark Souls", "Doom", "Skyrim", "Fallout 4",
            "Cyberpunk 2077", "Red Dead Redemption 2"];
        return names.Select((name, index) => new GameDetails(index + 1, name,
            name.ToLowerInvariant().Replace(' ', '-'), null, new DateOnly(2000 + index, 1, 1),
            ["PC de ejemplo", "Consola de ejemplo"],
            "Datos ficticios para desarrollo; no representan la ficha real del juego.",
            [index % 2 == 0 ? "Aventura de ejemplo" : "Acción de ejemplo"], [$"Estudio ficticio {index % 4 + 1}"], null))
            .ToArray();
    }
}
