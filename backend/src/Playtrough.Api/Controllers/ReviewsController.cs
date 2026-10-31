using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Reviews;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

/// <summary>Reseñas de juegos locales y operaciones sobre la reseña propia.</summary>
[ApiController]
[Route("api/games/{gameId:int}/reviews")]
[Tags("Reviews")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class ReviewsController(IReviewService reviews) : ControllerBase
{
    /// <summary>Listar las reseñas de un juego local.</summary>
    /// <remarks>Requiere un token válido de cualquier rol. Incluye todas las reseñas, también la propia.
    /// sort: recent (predeterminado, creación descendente), top (puntuación descendente), low (puntuación ascendente).
    /// Los empates usan creación descendente e ID descendente. page empieza en 1; pageSize es 10 por defecto y admite 1–30.
    /// El autor solo contiene userId y username; isMine se calcula desde el token. No requiere biblioteca.
    /// El resumen y la página comparten una transacción consistente. Las escrituras entre peticiones pueden desplazar páginas.</remarks>
    /// <response code="200">Reseñas paginadas, ratingSummary global del juego y hasMore. Sin reseñas: items vacío, average null y count 0.</response>
    /// <response code="400">Orden o paginación inválidos; extensions.code: invalid_query.</response>
    /// <response code="404">El juego no existe en Games; extensions.code: game_not_found.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ReviewListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(int gameId, [FromQuery] ReviewListRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        return Ok(await reviews.ListAsync(userId, gameId, request, ct));
    }

    /// <summary>Consultar la reseña propia de un juego local.</summary>
    /// <remarks>Requiere un token válido de cualquier rol. Solo consulta la reseña del usuario del token.</remarks>
    /// <response code="200">Reseña propia; text y editedAt pueden ser null.</response>
    /// <response code="404">No existe una reseña propia; extensions.code: review_not_found.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(MyReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMine(int gameId, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        var result = await reviews.GetMineAsync(userId, gameId, ct);
        return Ok(MyReviewDto.FromReview(result.Review));
    }

    /// <summary>Crear o editar la reseña propia.</summary>
    /// <remarks>Requiere un token válido de cualquier rol. gameId es local y no exige biblioteca.
    /// rating es un número de 1 a 5 en pasos de 0,5. text es opcional, nullable y de texto plano,
    /// con hasta 2000 caracteres Unicode después de normalizar controles, saltos de línea y espacios exteriores.
    /// Vacío equivale a null. Una edición idéntica no cambia fechas ni contadores.
    /// Presupuesto reviews-write: 20 escrituras por minuto por usuario, compartido con DELETE.</remarks>
    /// <response code="201">Se creó la reseña; incluye ratingSummary actualizado y Location de la reseña propia.</response>
    /// <response code="200">Reseña actualizada o idéntica; incluye ratingSummary actualizado.</response>
    /// <response code="400">Puntuación ausente, no numérica o inválida: invalid_rating. Texto no cadena o demasiado largo: invalid_text.</response>
    /// <response code="404">El juego no existe en Games; extensions.code: game_not_found.</response>
    /// <response code="409">Persiste un conflicto después del reintento; extensions.code: concurrent_modification.</response>
    [HttpPut("me")]
    [EnableRateLimiting(RateLimitPolicies.ReviewsWrite)]
    [ProducesResponseType(typeof(UpsertReviewResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(UpsertReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upsert(int gameId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertReviewRequest? request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        if (request is null) throw new ReviewException("invalid_rating");
        var result = await reviews.UpsertAsync(userId, gameId, request.ReadRating(), request.ReadText(), ct);
        var response = new UpsertReviewResponse(MyReviewDto.FromReview(result.Review), result.RatingSummary);
        return result.Created ? CreatedAtAction(nameof(GetMine), new { gameId }, response) : Ok(response);
    }

    /// <summary>Eliminar definitivamente la reseña propia.</summary>
    /// <remarks>Requiere un token válido de cualquier rol. Devuelve el resumen actualizado.
    /// Presupuesto reviews-write: 20 escrituras por minuto por usuario, compartido con PUT.</remarks>
    /// <response code="200">Reseña eliminada y ratingSummary actualizado; average es null cuando count es 0.</response>
    /// <response code="404">No existe una reseña propia; extensions.code: review_not_found.</response>
    /// <response code="409">Persiste un conflicto después del reintento; extensions.code: concurrent_modification.</response>
    [HttpDelete("me")]
    [EnableRateLimiting(RateLimitPolicies.ReviewsWrite)]
    [ProducesResponseType(typeof(DeleteReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int gameId, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        return Ok(new DeleteReviewResponse(await reviews.DeleteAsync(userId, gameId, ct)));
    }

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
