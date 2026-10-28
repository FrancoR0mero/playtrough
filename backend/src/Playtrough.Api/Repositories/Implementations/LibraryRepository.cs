using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Repositories.Implementations;

public sealed class LibraryRepository(AppDbContext db) : ILibraryRepository
{
    public Task<bool> GameExistsAsync(int gameId, CancellationToken ct = default)
        => db.Games.AnyAsync(game => game.Id == gameId, ct);

    public Task<LibraryEntry?> FindAsync(int userId, int gameId, CancellationToken ct = default)
        => db.LibraryEntries.AsNoTracking().SingleOrDefaultAsync(entry => entry.UserId == userId && entry.GameId == gameId, ct);

    public async Task<LibraryEntryDto> DescribeAsync(LibraryEntry entry, CancellationToken ct = default)
    {
        var game = await db.Games.AsNoTracking().SingleAsync(game => game.Id == entry.GameId, ct);
        // Preserve the mutation's result even if another request changes the entry before this read.
        return Describe(entry, game);
    }

    public async Task<LibraryListResponseDto> ListAsync(int userId, LibraryListRequest request, CancellationToken ct = default)
    {
        // Both reads share a snapshot: counts are global to this user, never to the filters.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var own = db.LibraryEntries.AsNoTracking().Where(entry => entry.UserId == userId);
        var groups = await own.GroupBy(entry => entry.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        int Count(LibraryStatus status) => groups.SingleOrDefault(group => group.Status == status)?.Count ?? 0;
        var counts = new LibraryCountsDto(Count(LibraryStatus.Playing), Count(LibraryStatus.Backlog),
            Count(LibraryStatus.Beaten), Count(LibraryStatus.Completed));

        if (!string.IsNullOrEmpty(request.Status))
        {
            var status = LibraryValues.ParseStatus(request.Status);
            own = own.Where(entry => entry.Status == status);
        }
        var query = from entry in own join game in db.Games.AsNoTracking() on entry.GameId equals game.Id
            select new { Entry = entry, Game = game };
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var name = request.Q.Trim().ToLowerInvariant();
            query = query.Where(row => row.Game.Name.ToLower().Contains(name));
        }
        var ordered = request.Sort switch
        {
            "name" => query.OrderBy(row => row.Game.Name).ThenBy(row => row.Game.Id),
            "release" => query.OrderByDescending(row => row.Game.FirstReleaseDate).ThenBy(row => row.Game.Id),
            "added" => query.OrderByDescending(row => row.Entry.AddedAt).ThenByDescending(row => row.Game.Id),
            _ => query.OrderByDescending(row => row.Entry.StatusChangedOn)
                .ThenByDescending(row => row.Entry.AddedAt).ThenByDescending(row => row.Game.Id),
        };
        var offset = ((long)request.Page - 1) * request.PageSize;
        var rows = offset > int.MaxValue ? [] : await ordered.Skip((int)offset).Take(request.PageSize + 1).ToListAsync(ct);
        var items = rows.Take(request.PageSize).Select(row => Describe(row.Entry, row.Game)).ToArray();
        await transaction.CommitAsync(ct);
        return new(items, counts, request.Page, request.PageSize, rows.Count > request.PageSize);
    }

    private static LibraryEntryDto Describe(LibraryEntry entry, Game game) => new(game.Id, game.ExternalId,
        game.Name, game.Slug, game.CoverImageUrl, game.FirstReleaseDate?.Year,
        LibraryValues.StatusValue(entry.Status), entry.StatusChangedOn, entry.AddedAt);

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
