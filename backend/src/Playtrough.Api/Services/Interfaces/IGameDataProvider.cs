using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

public interface IGameDataProvider
{
    Task<GameSearchResult> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default);
    // A missing game is distinct from provider failure; persistence will translate null to a 404.
    Task<GameDetails?> GetByExternalIdAsync(int externalId, CancellationToken cancellationToken = default);
    async Task<GameSearchResult> SuggestAsync(string query, int limit, CancellationToken cancellationToken = default)
        => await SearchAsync(query, 1, limit, cancellationToken);
    Task<IReadOnlyList<GameSummary>> GetSimilarAsync(int externalId, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<GameSummary>>([]);
    Task<int?> MatchAsync(GameMatchRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult<int?>(null);
}
