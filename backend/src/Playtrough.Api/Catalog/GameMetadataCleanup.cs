using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;

namespace Playtrough.Api.Catalog;

public sealed class GameMetadataCleanup(IServiceScopeFactory scopes, TimeProvider time, ILogger<GameMetadataCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = time.GetUtcNow().UtcDateTime;
                var expired = await db.Games.Where(game => game.MetadataExpiresAt <= now).Take(200).ToListAsync(stoppingToken);
                foreach (var game in expired) GameMetadata.Expire(game, now);
                await db.SaveChangesAsync(stoppingToken);
                if (expired.Count == 200) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Metadata cleanup failed: {Type}.", error.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken);
        }
    }
}
