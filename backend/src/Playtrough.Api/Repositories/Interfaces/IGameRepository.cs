using Playtrough.Api.Models;

namespace Playtrough.Api.Repositories.Interfaces;

public interface IGameRepository
{
    Task<Game?> FindAsync(string provider, int externalId, CancellationToken cancellationToken = default);
    Task<Game> InsertOrGetExistingAsync(Game game, CancellationToken cancellationToken = default);
    Task UpdateAsync(Game game, CancellationToken cancellationToken = default);
}
