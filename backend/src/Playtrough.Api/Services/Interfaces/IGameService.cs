using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Interfaces;

public interface IGameService
{
    Task<Game> GetOrCreateAsync(string provider, int externalId, CancellationToken cancellationToken = default);
}
