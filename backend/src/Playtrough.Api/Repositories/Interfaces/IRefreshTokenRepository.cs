using Playtrough.Api.Models;

namespace Playtrough.Api.Repositories.Interfaces;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
}
