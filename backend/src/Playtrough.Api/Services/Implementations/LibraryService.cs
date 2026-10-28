using Playtrough.Api.Models;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class LibraryService(ILibraryRepository repository, TimeProvider time) : ILibraryService
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

    public Task<LibraryListResponseDto> ListAsync(int userId, LibraryListRequest request, CancellationToken ct = default)
        => repository.ListAsync(userId, request, ct);

    private static LibraryEvent Event(LibraryEntry entry, LibraryEventType type, LibraryStatus? from, LibraryStatus? to,
        DateOnly date, DateTime now) => new()
    {
        UserId = entry.UserId, GameId = entry.GameId, Type = type,
        FromStatus = from, ToStatus = to, OccurredOn = date, RecordedAt = now,
    };
}
