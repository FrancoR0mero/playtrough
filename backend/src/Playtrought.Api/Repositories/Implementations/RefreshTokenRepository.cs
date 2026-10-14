using Playtrought.Api.Data;
using Playtrought.Api.Models;
using Playtrought.Api.Repositories.Interfaces;

namespace Playtrought.Api.Repositories.Implementations;

public class RefreshTokenRepository(AppDbContext dbContext) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(ct);
    }
}
