using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

public interface IGameCatalogProvider
{
    Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default);
    // A missing game is distinct from provider failure; persistence will translate null to a 404.
    Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default);
}
