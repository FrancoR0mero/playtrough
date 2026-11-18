using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/timeline/entries"), Tags("Reactions"), Authorize]
[EnableRateLimiting(RateLimitPolicies.ReactionsWrite)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ReactionSummaryDto), 200)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class TimelineReactionsController(ReactionService reactions, TimelineReactionTarget target) : ControllerBase
{
    /// <summary>Dar o cambiar mi reacción a una entrada visible.</summary>
    /// <remarks>El cuerpo admite {"value":"like"} o {"value":"dislike"}. Repetir el mismo valor es idempotente.
    /// Requiere acceso actual a la entrada. Comparte reactions-write, 60 escrituras por minuto por usuario.</remarks>
    /// <response code="400">invalid_reaction: se requiere like o dislike.</response>
    /// <response code="404">entry_not_found: entrada inexistente o no visible.</response>
    /// <response code="409">cannot_react_to_own_content: la entrada pertenece al usuario actual.</response>
    [HttpPut("{id:long}/reaction")]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Set(long id, [FromBody] SetReactionRequest request, CancellationToken ct)
        => ActorId() is { } actor ? Ok(await reactions.SetReactionAsync(actor, id, target, request, ct)) : Challenge();

    /// <summary>Quitar mi reacción a una entrada visible.</summary>
    /// <remarks>Si no había reacción devuelve el mismo resumen. Comparte reactions-write con PUT.</remarks>
    /// <response code="404">entry_not_found: entrada inexistente o no visible.</response>
    [HttpDelete("{id:long}/reaction")]
    public async Task<IActionResult> Remove(long id, CancellationToken ct)
        => ActorId() is { } actor ? Ok(await reactions.RemoveReactionAsync(actor, id, target, ct)) : Challenge();

    private int? ActorId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var actor) && actor > 0 ? actor : null;
}
