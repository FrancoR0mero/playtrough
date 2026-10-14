using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;

namespace Playtrough.Api.Repositories.Implementations;

public class RefreshTokenRepository(AppDbContext dbContext) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(ct);
    }
}
