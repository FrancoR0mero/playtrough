using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Games;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Catalog;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Controllers;

/// <summary>Búsqueda del catálogo y fichas de juegos usados localmente.</summary>
[ApiController]
[Route("api/games")]
[Tags("Games")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Catalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class GamesController(IGameDataProvider catalog, IGameService games, IReviewService reviews,
    AppDbContext db, IOptions<CatalogSettings> settings, TimeProvider time) : ControllerBase
{
    /// <summary>Buscar juegos por nombre sin persistir resultados.</summary>
    /// <remarks>Requiere un usuario autenticado, con cualquier rol. q debe tener de 2 a 100 caracteres tras recortar; page comienza en 1 y pageSize admite de 1 a 40 (predeterminado 20). Devuelve hasta cinco plataformas por juego y hasMore indica si existe otra página. Las imágenes son arte o capturas normalmente horizontales.</remarks>
    /// <response code="200">Resultados del proveedor, año de lanzamiento nullable y paginación.</response>
    /// <response code="400">Consulta o paginación inválida; extensions.code: invalid_query.</response>
    /// <response code="429">Presupuesto catalog compartido entre búsqueda y detalle por usuario; extensions.code: rate_limited.</response>
    /// <response code="503">Proveedor no disponible o deshabilitado; extensions.code: catalog_unavailable.</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(GameSearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] GameSearchRequest request, CancellationToken ct)
    {
        var result = await catalog.SearchAsync(request.Q!.Trim(), request.Page, request.PageSize, ct);
        return Ok(new GameSearchResponseDto(result.Items.Select(game => new GameSummaryDto(game.ExternalId,
            game.Name, game.Slug, game.CoverUrl, game.ReleaseYear ?? game.ReleaseDate?.Year, game.Platforms.Take(5).ToArray()) { SourceUrl = game.SourceUrl }).ToArray(),
            result.Page, result.PageSize, result.HasMore, result.IsLimited));
    }

    /// <summary>Consultar y guardar la ficha de un juego por su ID externo.</summary>
    /// <remarks>id es local; externalId pertenece a GameBrain. Los metadatos del proveedor caducan en 24 horas.</remarks>
    /// <response code="200">Ficha persistida con ID local, fecha nullable y metadatos. ratingSummary se lee siempre de Games: average a un decimal o null si count es 0.</response>
    /// <response code="400">El ID externo debe ser un entero positivo; extensions.code: invalid_game_id.</response>
    /// <response code="404">El proveedor indica que el juego no existe; extensions.code: game_not_found.</response>
    /// <response code="429">Presupuesto catalog por usuario agotado; extensions.code: rate_limited.</response>
    /// <response code="503">Proveedor no disponible y sin copia local; extensions.code: catalog_unavailable.</response>
    [HttpGet("{externalId}")]
    [ProducesResponseType(typeof(GameDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail([Range(1, int.MaxValue)] int externalId, CancellationToken ct)
    {
        var game = await games.GetOrCreateAsync(settings.Value.UseFake ? "fake" : "gamebrain", externalId, ct);
        return await Describe(game, ct);
    }

    /// <summary>Autocompletar juegos con hasta diez sugerencias.</summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions([FromQuery, Required, StringLength(100, MinimumLength = 2)] string q,
        [FromQuery, Range(1, 10)] int limit = 10, CancellationToken ct = default)
    {
        if (q.Trim().Length < 2 || q.Any(char.IsControl)) return BadRequest();
        var result = await catalog.SuggestAsync(q.Trim(), limit, ct);
        return Ok(new GameSearchResponseDto(result.Items.Select(game => new GameSummaryDto(game.ExternalId, game.Name,
            game.Slug, game.CoverUrl, game.ReleaseYear ?? game.ReleaseDate?.Year, game.Platforms) { SourceUrl = game.SourceUrl }).ToArray(),
            1, limit, false, result.IsLimited));
    }

    /// <summary>Abrir una ficha por su identidad local, incluso antes del backfill.</summary>
    [HttpGet("local/{gameId:int}")]
    public async Task<IActionResult> LocalDetail([Range(1, int.MaxValue)] int gameId, CancellationToken ct)
    {
        var game = await db.Games.SingleOrDefaultAsync(game => game.Id == gameId, ct) ?? throw new GameNotFoundException();
        if (game.GameBrainId is { } id) game = await games.GetOrCreateAsync("gamebrain", id, ct);
        return await Describe(game, ct);
    }

    /// <summary>Consultar juegos similares.</summary>
    [HttpGet("{externalId:int}/similar")]
    public async Task<IActionResult> Similar([Range(1, int.MaxValue)] int externalId,
        [FromQuery, Range(1, 50)] int limit = 10, CancellationToken ct = default)
        => Ok(await catalog.GetSimilarAsync(externalId, limit, ct));

    private async Task<IActionResult> Describe(Game game, CancellationToken ct)
    {
        if (GameMetadata.Expire(game, time.GetUtcNow().UtcDateTime)) await db.SaveChangesAsync(ct);
        var ratingSummary = await reviews.GetSummaryAsync(game.Id, ct);
        var metadata = game.ProviderDetailsJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<GameDetails>(game.ProviderDetailsJson);
        return Ok(new GameDetailsDto(game.Id, game.GameBrainId ?? game.ExternalId, game.Name, game.Slug, game.Summary,
            game.CoverImageUrl, game.CoverImageUrl, game.FirstReleaseDate, game.Platforms, game.Genres, game.Developers, ratingSummary)
        {
            SourceUrl = game.SourceUrl, MetadataUnavailable = game.Provider != "fake" && game.MetadataExpiresAt is null,
            ProviderRating = metadata?.ProviderRating, ProviderRatingCount = metadata?.ProviderRatingCount,
            CriticScore = metadata?.CriticScore, Screenshots = metadata?.Screenshots ?? [],
        });
    }
}
