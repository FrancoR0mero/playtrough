namespace Playtrough.Api.Services.Interfaces;

public interface IAvatarStorage
{
    Task<string> SaveAsync(byte[] image, CancellationToken ct);
    Task DeleteAsync(string fileName, CancellationToken ct);
    Stream? OpenRead(string fileName);
}
