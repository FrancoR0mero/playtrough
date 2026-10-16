using Playtrough.Api.Models;

namespace Playtrough.Api.Repositories.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task<bool> TryRevokeActiveAsync(string tokenHash, int userId, DateTime utcNow, CancellationToken ct = default);

    Task SetReplacementAsync(string tokenHash, string replacementHash, CancellationToken ct = default);

    Task RevokeAllActiveAsync(int userId, DateTime utcNow, CancellationToken ct = default);

    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    Task RevokeActiveAsync(string tokenHash, int userId, DateTime utcNow, CancellationToken ct = default);
}
