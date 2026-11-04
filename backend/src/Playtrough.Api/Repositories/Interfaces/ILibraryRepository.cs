using Playtrough.Api.Models;
using Playtrough.Api.Dtos.Library;

namespace Playtrough.Api.Repositories.Interfaces;

public interface ILibraryRepository
{
    Task<bool> GameExistsAsync(int gameId, CancellationToken ct = default);
    Task<LibraryEntry?> FindAsync(int userId, int gameId, CancellationToken ct = default);
    Task<LibraryEntryDto> DescribeAsync(LibraryEntry entry, CancellationToken ct = default);
    Task<LibraryListResponseDto> ListAsync(int userId, LibraryListRequest request, CancellationToken ct = default);
    /// <summary>False means another insert won the unique user/game identity.</summary>
    Task<bool> TryAddAsync(LibraryEntry entry, LibraryEvent change, CancellationToken ct = default);
    /// <summary>False means the entry was changed or removed after it was read.</summary>
    Task<bool> TryChangeStatusAsync(LibraryEntry entry, Guid previousToken, LibraryEvent change, CancellationToken ct = default);
    Task<bool> TryRemoveAsync(LibraryEntry entry, LibraryEvent change, CancellationToken ct = default);
}
