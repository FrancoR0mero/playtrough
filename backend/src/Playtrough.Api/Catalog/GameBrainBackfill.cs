using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Catalog;

public sealed class GameBrainBackfill(AppDbContext db, IGameDataProvider provider, TimeProvider time)
{
    // JSON Lines permits flushing each outcome and resuming after interruption without losing the report.
    public async Task<int> RunAsync(TextWriter report, CancellationToken ct = default)
    {
        var ids = await db.Games.Where(game => game.Provider == "rawg" && game.GameBrainId == null)
            .OrderBy(game => game.Id).Select(game => game.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            var game = await db.Games.SingleAsync(game => game.Id == id, ct);
            if (game.GameBrainId is not null) continue;
            var requests = new List<GameMatchRequest>();
            if (game.SteamId is > 0) requests.Add(new(game.SteamId, null, null, null));
            if (game.IgdbId is > 0) requests.Add(new(null, game.IgdbId, null, null));
            if (!string.IsNullOrWhiteSpace(game.Name) && game.FirstReleaseDate is { } date)
                requests.Add(new(null, null, game.Name, date.Year));
            int? match = null;
            var method = "none";
            try
            {
                foreach (var request in requests)
                {
                    method = request.SteamId is not null ? "steam_id" : request.IgdbId is not null ? "igdb_id" : "name_year";
                    match = await provider.MatchAsync(request, ct);
                    await Task.Delay(TimeSpan.FromSeconds(1.1), time, ct);
                    if (match is not null) break;
                    await Write("miss", null);
                }
                if (match is null) { await Write(requests.Count == 0 ? "missing_identifiers_or_year" : "unmatched", null); continue; }
                if (await db.Games.AnyAsync(other => other.GameBrainId == match && other.Id != id, ct))
                { await Write("collision_requires_review", match); continue; }
                game.GameBrainId = match;
                // Backfill writes identity only, never overwrites legacy metadata or local foreign keys.
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    if (!await db.Games.AnyAsync(other => other.GameBrainId == match, ct)) throw;
                    await Write("concurrent_collision_requires_review", match);
                    continue;
                }
                await Write("matched", match);
            }
            catch (GameBrainQuotaException) { await Write("quota_exhausted_resume_later", null); return 2; }
            catch (GameBrainAuthenticationException) { await Write("credentials_rejected", null); return 3; }
            catch (GameCatalogUnavailableException) { await Write("provider_unavailable_resume_later", null); return 4; }

            async Task Write(string status, int? gameBrainId)
            {
                await report.WriteLineAsync(JsonSerializer.Serialize(new { gameId = id, legacyId = game.ExternalId,
                    game.Name, year = game.FirstReleaseDate?.Year, method, status, gameBrainId }));
                await report.FlushAsync(ct);
            }
        }
        return 0;
    }
}
