using Playtrough.Api.Models;
using Playtrough.Api.Dtos.Library;

namespace Playtrough.Api.Services.Interfaces;

public interface ILibraryService
{
    Task<LibraryEntry> AddAsync(int userId, int gameId, LibraryStatus status, DateOnly? occurredOn = null,
        CancellationToken ct = default);
    Task<LibraryEntry> ChangeStatusAsync(int userId, int gameId, LibraryStatus newStatus, DateOnly? occurredOn = null,
        CancellationToken ct = default);
    Task RemoveAsync(int userId, int gameId, CancellationToken ct = default);
    Task<LibraryEntry?> GetEntryAsync(int userId, int gameId, CancellationToken ct = default);
    Task<LibraryEntryDto> DescribeAsync(LibraryEntry entry, CancellationToken ct = default);
    Task<LibraryListResponseDto> ListAsync(int userId, LibraryListRequest request, CancellationToken ct = default);
}
