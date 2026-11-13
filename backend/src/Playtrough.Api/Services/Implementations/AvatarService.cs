using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class AvatarService(AppDbContext db, AvatarImageProcessor processor, IAvatarStorage storage, ILogger<AvatarService> logger) : IAvatarService
{
    public async Task<string?> UploadAsync(int userId, IFormFile file, CancellationToken ct)
    {
        var user = await ReadAsync(userId, ct);
        var image = await processor.ProcessAsync(file, ct);
        var key = await storage.SaveAsync(image, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var stamp = Guid.NewGuid().ToString();
            if (await db.Users.Where(u => u.Id == userId && u.ConcurrencyStamp == user.ConcurrencyStamp)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.AvatarFileName, key)
                    .SetProperty(u => u.ConcurrencyStamp, stamp), CancellationToken.None) != 1)
                throw new ProfileException("concurrent_modification");
        }
        catch
        {
            await CleanupAsync(key);
            throw;
        }
        await CleanupAsync(user.AvatarFileName);
        return AvatarUrls.FromFile(key);
    }

    public async Task DeleteAsync(int userId, CancellationToken ct)
    {
        var user = await ReadAsync(userId, ct);
        if (user.AvatarFileName is null) return;
        ct.ThrowIfCancellationRequested();
        var stamp = Guid.NewGuid().ToString();
        if (await db.Users.Where(u => u.Id == userId && u.ConcurrencyStamp == user.ConcurrencyStamp)
            .ExecuteUpdateAsync(update => update.SetProperty(u => u.AvatarFileName, (string?)null)
                .SetProperty(u => u.ConcurrencyStamp, stamp), CancellationToken.None) != 1)
            throw new ProfileException("concurrent_modification");
        await CleanupAsync(user.AvatarFileName);
    }

    private async Task<ApplicationUser> ReadAsync(int userId, CancellationToken ct)
        => await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new ProfileException("user_not_found");

    private async Task CleanupAsync(string? key)
    {
        if (key is null) return;
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (IOException) { logger.LogWarning("Avatar file cleanup failed for {AvatarKey}", key); }
        catch (UnauthorizedAccessException) { logger.LogWarning("Avatar file cleanup denied for {AvatarKey}", key); }
    }
}
