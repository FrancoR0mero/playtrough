using Playtrought.Api.Models;

namespace Playtrought.Api.Repositories.Interfaces;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
}
