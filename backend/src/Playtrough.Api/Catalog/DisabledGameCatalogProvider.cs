using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Catalog;

public sealed class DisabledGameCatalogProvider : IGameCatalogProvider
{
    public Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromException<GameSearchResult>(new GameCatalogUnavailableException());

    public Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default)
        => Task.FromException<GameDetails?>(new GameCatalogUnavailableException());
}
