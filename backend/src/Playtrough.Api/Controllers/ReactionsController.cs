using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Tags("Reactions"), Authorize]
[EnableRateLimiting(RateLimitPolicies.ReactionsWrite)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ReactionSummaryDto), 200)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class ReactionsController(ReactionService reactions, TimelineReactionTarget entries,
    TimelineCommentReactionTarget comments, AppDbContext db) : ControllerBase
{
    /// <summary>Dar o cambiar mi reacción a una entrada visible.</summary>
    /// <remarks>El cuerpo admite {"value":"like"} o {"value":"dislike"}. Repetir el mismo valor es idempotente.
    /// Requiere acceso actual a la entrada. Comparte reactions-write, 60 escrituras por minuto por usuario.</remarks>
    /// <response code="400">invalid_reaction: se requiere like o dislike.</response>
    /// <response code="404">entry_not_found: entrada inexistente o no visible.</response>
    /// <response code="409">cannot_react_to_own_content: la entrada pertenece al usuario actual.</response>
    [HttpPut("api/timeline/entries/{id:long}/reaction")]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public Task<IActionResult> Set(long id, [FromBody] SetReactionRequest request, CancellationToken ct)
        => SetReaction(id, entries, request, ct);

    /// <summary>Quitar mi reacción a una entrada visible.</summary>
    /// <remarks>Si no había reacción devuelve el mismo resumen. Comparte reactions-write con PUT.</remarks>
    /// <response code="404">entry_not_found: entrada inexistente o no visible.</response>
    [HttpDelete("api/timeline/entries/{id:long}/reaction")]
    public Task<IActionResult> Remove(long id, CancellationToken ct)
        => RemoveReaction(id, entries, ct);

    /// <summary>Dar o cambiar mi reacción a un comentario de una entrada visible.</summary>
    /// <remarks>Acepta like o dislike. Comparte el presupuesto reactions-write.</remarks>
    /// <response code="400">invalid_reaction: se requiere like o dislike.</response>
    /// <response code="404">comment_not_found: comentario inexistente o entrada no visible.</response>
    /// <response code="409">cannot_react_to_own_content: el comentario pertenece al usuario actual.</response>
    [HttpPut("api/timeline/comments/{id:long}/reaction")]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public Task<IActionResult> SetComment(long id, [FromBody] SetReactionRequest request, CancellationToken ct)
        => SetReaction(id, comments, request, ct);

    /// <summary>Quitar mi reacción a un comentario de una entrada visible.</summary>
    /// <response code="404">comment_not_found: comentario inexistente o entrada no visible.</response>
    [HttpDelete("api/timeline/comments/{id:long}/reaction")]
    public Task<IActionResult> RemoveComment(long id, CancellationToken ct) => RemoveReaction(id, comments, ct);

    /// <summary>Dar o cambiar mi reacción a una reseña visible del juego indicado.</summary>
    /// <remarks>Acepta like o dislike. Respeta el acceso a las reseñas del juego y comparte reactions-write.</remarks>
    /// <response code="400">invalid_reaction: se requiere like o dislike.</response>
    /// <response code="404">review_not_found: reseña inexistente, no visible o de otro juego.</response>
    /// <response code="409">cannot_react_to_own_content: la reseña pertenece al usuario actual.</response>
    [HttpPut("api/games/{gameId:int}/reviews/{reviewId:int}/reaction")]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public Task<IActionResult> SetReview(int gameId, int reviewId, [FromBody] SetReactionRequest request, CancellationToken ct)
        => SetReaction(reviewId, new ReviewReactionTarget(db, gameId), request, ct);

    /// <summary>Quitar mi reacción a una reseña visible del juego indicado.</summary>
    /// <response code="404">review_not_found: reseña inexistente, no visible o de otro juego.</response>
    [HttpDelete("api/games/{gameId:int}/reviews/{reviewId:int}/reaction")]
    public Task<IActionResult> RemoveReview(int gameId, int reviewId, CancellationToken ct)
        => RemoveReaction(reviewId, new ReviewReactionTarget(db, gameId), ct);

    private async Task<IActionResult> SetReaction<TReaction>(long id, IReactionTarget<TReaction> target, SetReactionRequest request,
        CancellationToken ct) where TReaction : class, IContentReaction
        => ActorId() is { } actor ? Ok(await reactions.SetReactionAsync(actor, id, target, request, ct)) : Challenge();

    private async Task<IActionResult> RemoveReaction<TReaction>(long id, IReactionTarget<TReaction> target, CancellationToken ct)
        where TReaction : class, IContentReaction
        => ActorId() is { } actor ? Ok(await reactions.RemoveReactionAsync(actor, id, target, ct)) : Challenge();

    private int? ActorId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var actor) && actor > 0 ? actor : null;
}
