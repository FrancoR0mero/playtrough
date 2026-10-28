using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;

namespace Playtrough.Api.Repositories.Implementations;

public sealed class LibraryRepository(AppDbContext db) : ILibraryRepository
{
    public Task<bool> GameExistsAsync(int gameId, CancellationToken ct = default)
        => db.Games.AnyAsync(game => game.Id == gameId, ct);

    public Task<LibraryEntry?> FindAsync(int userId, int gameId, CancellationToken ct = default)
        => db.LibraryEntries.AsNoTracking().SingleOrDefaultAsync(entry => entry.UserId == userId && entry.GameId == gameId, ct);

    public async Task<bool> TryAddAsync(LibraryEntry entry, LibraryEvent change, CancellationToken ct = default)
    {
        db.LibraryEntries.Add(entry);
        db.LibraryEvents.Add(change);
        try
        {
            // EF wraps both statements in one transaction; never save the event separately.
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            Detach(entry, change);
            // Follow GameRepository's race recovery: reread the unique identity, without
            // parsing provider messages. Other persistence failures must still propagate.
            if (await FindAsync(entry.UserId, entry.GameId, ct) is not null) return false;
            throw;
        }
        finally { Detach(entry, change); }
    }

    public async Task<bool> TryChangeStatusAsync(LibraryEntry entry, Guid previousToken, LibraryEvent change,
        CancellationToken ct = default)
    {
        var tracked = db.LibraryEntries.Attach(entry);
        tracked.Property(value => value.Status).IsModified = true;
        tracked.Property(value => value.StatusChangedOn).IsModified = true;
        tracked.Property(value => value.ConcurrencyToken).IsModified = true;
        tracked.Property(value => value.ConcurrencyToken).OriginalValue = previousToken;
        db.LibraryEvents.Add(change);
        return await SaveMutationAsync(entry, change, ct);
    }

    public async Task<bool> TryRemoveAsync(LibraryEntry entry, LibraryEvent change, CancellationToken ct = default)
    {
        db.LibraryEntries.Remove(entry);
        db.LibraryEvents.Add(change);
        return await SaveMutationAsync(entry, change, ct);
    }

    private async Task<bool> SaveMutationAsync(LibraryEntry entry, LibraryEvent change, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException) when (!ct.IsCancellationRequested) { return false; }
        // A failed event or concurrency check rolls back the entire SaveChanges transaction.
        // Detach both objects so a retry (or later save in this scope) cannot leak an event.
        finally { Detach(entry, change); }
    }

    private void Detach(LibraryEntry entry, LibraryEvent change)
    {
        db.Entry(entry).State = EntityState.Detached;
        db.Entry(change).State = EntityState.Detached;
    }
}
