namespace Playtrough.Api.Services.Interfaces;

public interface IAvatarService
{
    Task<string?> UploadAsync(int userId, IFormFile file, CancellationToken ct);
    Task DeleteAsync(int userId, CancellationToken ct);
}
