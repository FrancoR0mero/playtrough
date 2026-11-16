using Playtrough.Api.Models;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Playtrough.Api.Catalog;

namespace Playtrough.Api.Services.Implementations;

public sealed class LibraryService(ILibraryRepository repository, TimeProvider time, IGameService games) : ILibraryService
{
    public async Task<LibraryEntry> AddAsync(int userId, int gameId, LibraryStatus status, DateOnly? occurredOn = null,
        CancellationToken ct = default)
    {
        _ = LibraryValues.StatusValue(status);
        var now = time.GetUtcNow().UtcDateTime;
        var date = LibraryValues.EffectiveDate(occurredOn, now);
        if (!await repository.GameExistsAsync(gameId, ct)) throw new LibraryException("game_not_found");
        if (await repository.FindAsync(userId, gameId, ct) is not null) throw new LibraryException("already_in_library");

        var entry = new LibraryEntry
        {
            UserId = userId, GameId = gameId, Status = status, AddedAt = now,
            StatusChangedOn = date, ConcurrencyToken = Guid.NewGuid(),
        };
        var change = Event(entry, LibraryEventType.Added, null, status, date, now);
        if (!await repository.TryAddAsync(entry, change, ct)) throw new LibraryException("already_in_library");
        return entry;
    }

    public async Task<LibraryEntry> ChangeStatusAsync(int userId, int gameId, LibraryStatus newStatus,
        DateOnly? occurredOn = null, CancellationToken ct = default)
    {
        _ = LibraryValues.StatusValue(newStatus);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var entry = await repository.FindAsync(userId, gameId, ct) ?? throw new LibraryException("not_in_library");
            // Same-status requests do not validate effective dates or touch any field.
            if (entry.Status == newStatus) return entry;
            var now = time.GetUtcNow().UtcDateTime;
            var date = LibraryValues.EffectiveDate(occurredOn, now, entry.StatusChangedOn);
            var change = Event(entry, LibraryEventType.StatusChanged, entry.Status, newStatus, date, now);
            var previousToken = entry.ConcurrencyToken;
            entry.Status = newStatus;
            entry.StatusChangedOn = date;
            entry.ConcurrencyToken = Guid.NewGuid();
            if (await repository.TryChangeStatusAsync(entry, previousToken, change, ct)) return entry;
            // Retry from a fresh read and repeat all business validation against the winner.
        }
        throw new LibraryException("concurrent_modification");
    }

    public async Task RemoveAsync(int userId, int gameId, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var entry = await repository.FindAsync(userId, gameId, ct) ?? throw new LibraryException("not_in_library");
            var now = time.GetUtcNow().UtcDateTime;
            var change = Event(entry, LibraryEventType.Removed, entry.Status, null, DateOnly.FromDateTime(now), now);
            if (await repository.TryRemoveAsync(entry, change, ct)) return;
        }
        throw new LibraryException("concurrent_modification");
    }

    public Task<LibraryEntry?> GetEntryAsync(int userId, int gameId, CancellationToken ct = default)
        => repository.FindAsync(userId, gameId, ct);

    public Task<LibraryEntryDto> DescribeAsync(LibraryEntry entry, CancellationToken ct = default)
        => repository.DescribeAsync(entry, ct);

    public async Task<LibraryListResponseDto> ListAsync(int userId, LibraryListRequest request, CancellationToken ct = default)
    {
        var response = await repository.ListAsync(userId, request, ct);
        if (response.Items.Count == 0) return response;
        var ids = await repository.MetadataRefreshIdsAsync(response.Items.Select(item => item.GameId).ToArray(),
            time.GetUtcNow().UtcDateTime, ct);
        if (ids.Count == 0) return response;

        // The list's snapshot transaction is already committed. Never hold database locks
        // across provider requests, or issue parallel operations on the scoped DbContext.
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var game = await games.GetOrCreateAsync(GameService.GameBrainProvider, id, ct);
                // GameService returns the local identity on quota/unavailability. Avoid
                // repeatedly asking an unavailable provider for the rest of this page.
                if (game.MetadataExpiresAt is not { } expiry || expiry <= time.GetUtcNow().UtcDateTime) break;
            }
            catch (GameNotFoundException) { /* Keep the entry even if the provider removed its game. */ }
            catch (GameCatalogUnavailableException) { break; }
            catch (GameBrainAuthenticationException) { break; }
        }

        // Reapply filters and ordering against refreshed names and release dates.
        return await repository.ListAsync(userId, request, ct);
    }

    private static LibraryEvent Event(LibraryEntry entry, LibraryEventType type, LibraryStatus? from, LibraryStatus? to,
        DateOnly date, DateTime now) => new()
    {
        UserId = entry.UserId, GameId = entry.GameId, Type = type,
        FromStatus = from, ToStatus = to, OccurredOn = date, RecordedAt = now,
    };
}
