using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;

namespace Playtrough.Api.Repositories.Implementations;

public sealed class GameRepository(AppDbContext db) : IGameRepository
{
    public Task<Game?> FindAsync(string provider, int externalId, CancellationToken cancellationToken = default)
        => db.Games.SingleOrDefaultAsync(game => game.Provider == provider && game.ExternalId == externalId, cancellationToken);

    public async Task<Game> InsertOrGetExistingAsync(Game game, CancellationToken cancellationToken = default)
    {
        db.Games.Add(game);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return game;
        }
        catch (DbUpdateException) when (!cancellationToken.IsCancellationRequested)
        {
            // The unique index arbitrates races across API instances. No transaction
            // spans the preceding read/provider call, so this reread sees the winner.
            // Detach only our failed insert; never leave it pending for a later save.
            db.Entry(game).State = EntityState.Detached;
            var existing = await FindAsync(game.Provider, game.ExternalId, cancellationToken);
            if (existing is not null) return existing;
            // No matching persisted row: this was not a recoverable insertion race.
            // Preserve the original failure, without parsing provider-specific messages.
            throw;
        }
    }

    public async Task UpdateAsync(Game game, CancellationToken cancellationToken = default)
    {
        db.Games.Update(game);
        await db.SaveChangesAsync(cancellationToken);
    }
}
