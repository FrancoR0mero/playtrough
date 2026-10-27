using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Games;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Interfaces;

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
public sealed class GamesController(IGameCatalogProvider catalog, IGameService games) : ControllerBase
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
            game.Name, game.Slug, game.CoverUrl, game.ReleaseDate?.Year, game.Platforms.Take(5).ToArray())).ToArray(),
            result.Page, result.PageSize, result.HasMore, result.IsLimited));
    }

    /// <summary>Consultar y guardar la ficha de un juego por su ID externo.</summary>
    /// <remarks>Requiere un usuario autenticado, con cualquier rol. id es el ID local; externalId pertenece a RAWG. El resumen usa texto plano, hasta 3000 caracteres. Devuelve o crea la ficha y refresca copias viejas; ante una caída del proveedor puede devolver la copia local. coverUrl y coverUrlLarge conservan la imagen original horizontal, o null si no existe.</remarks>
    /// <response code="200">Ficha persistida con ID local, fecha nullable y metadatos.</response>
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
        var game = await games.GetOrCreateAsync("rawg", externalId, ct);
        return Ok(new GameDetailsDto(game.Id, game.ExternalId, game.Name, game.Slug, game.Summary,
            game.CoverImageUrl, game.CoverImageUrl, game.FirstReleaseDate, game.Platforms, game.Genres, game.Developers));
    }
}
