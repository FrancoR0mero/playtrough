using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class LocalAvatarStorage(IWebHostEnvironment environment, IOptions<AvatarSettings> settings) : IAvatarStorage
{
    private readonly string root = Path.GetFullPath(settings.Value.StoragePath, environment.ContentRootPath);

    public async Task<string> SaveAsync(byte[] image, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var name = $"{Guid.NewGuid():N}.webp";
        var path = Path.Combine(root, name);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await file.WriteAsync(image, ct);
            return name;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public Task DeleteAsync(string fileName, CancellationToken ct)
    {
        if (!AvatarUrls.IsFileName(fileName)) throw new ArgumentException("Invalid avatar key.");
        File.Delete(Path.Combine(root, fileName));
        return Task.CompletedTask;
    }

    public Stream? OpenRead(string fileName)
    {
        if (!AvatarUrls.IsFileName(fileName)) return null;
        try { return new FileStream(Path.Combine(root, fileName), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
