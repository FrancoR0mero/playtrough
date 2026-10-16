using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;

namespace Playtrough.Api.Repositories.Implementations;

public class RefreshTokenRepository(AppDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
        => dbContext.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, ct);

    public async Task RevokeActiveAsync(string tokenHash, int userId, DateTime utcNow, CancellationToken ct = default)
        => await TryRevokeActiveAsync(tokenHash, userId, utcNow, ct);

    public async Task<bool> TryRevokeActiveAsync(string tokenHash, int userId, DateTime utcNow, CancellationToken ct = default)
    {
        // One conditional UPDATE makes revocation atomic, including concurrent logouts.
        // Match IsActive's rule in SQL and leave an existing RevokedAt unchanged.
        var affected = await dbContext.RefreshTokens
            .Where(token => token.TokenHash == tokenHash && token.UserId == userId &&
                token.RevokedAt == null && token.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTime?)utcNow), ct);
        return affected == 1;
    }

    public async Task SetReplacementAsync(string tokenHash, string replacementHash, CancellationToken ct = default)
    {
        var affected = await dbContext.RefreshTokens.Where(token => token.TokenHash == tokenHash &&
                token.RevokedAt != null && token.ReplacedByToken == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ReplacedByToken, replacementHash), ct);
        if (affected != 1) throw new InvalidOperationException("The consumed refresh token could not be linked to its replacement.");
    }

    public async Task RevokeAllActiveAsync(int userId, DateTime utcNow, CancellationToken ct = default)
    {
        await dbContext.RefreshTokens.Where(token => token.UserId == userId &&
                token.RevokedAt == null && token.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTime?)utcNow), ct);
    }

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(ct);
    }
}
