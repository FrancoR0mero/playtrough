using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/timeline/entries"), Tags("Timeline"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class TimelineVisibilityController(ITimelineService timeline) : ControllerBase
{
    /// <summary>Ocultar o mostrar una entrada propia de la línea de tiempo.</summary>
    /// <remarks>Solo el dueño. Cuerpo obligatorio: { "hidden": true } o { "hidden": false }.
    /// Idempotente. Las ocultas siguen visibles para su dueño y desaparecen de líneas y períodos ajenos.
    /// No modifica la reseña, el evento ni las fechas. Política timeline-write: 30/minuto/usuario,
    /// independiente del presupuesto de lectura. Nunca recibe un ID de usuario como autor.</remarks>
    /// <response code="400">invalid_body: cuerpo ausente, inválido o hidden no booleano.</response>
    /// <response code="404">entry_not_found: entrada inexistente o perteneciente a otro usuario.</response>
    [HttpPut("{id:long}/visibility"), EnableRateLimiting(RateLimitPolicies.TimelineWrite)]
    [ProducesResponseType(typeof(TimelineEntryDto), 200)]
    public async Task<IActionResult> SetVisibility(long id, [FromBody] UpdateTimelineVisibilityRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var owner) || owner <= 0) return Challenge();
        return Ok(await timeline.SetVisibilityAsync(owner, id, request, ct));
    }
}
