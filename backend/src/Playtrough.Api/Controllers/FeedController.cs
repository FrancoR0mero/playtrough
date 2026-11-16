using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/feed"), Tags("Feed"), Authorize]
[EnableRateLimiting(RateLimitPolicies.TimelineRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class FeedController(IFeedService feed) : ControllerBase
{
    /// <summary>Consultar la actividad reciente y visible de los amigos.</summary>
    /// <remarks>Orden: recordedAt e id descendentes. Cursor protegido v1 vinculado al visitante.
    /// limit: 20 por defecto, máximo 50. Incluye autor, juego y reseña actual cuando corresponde.
    /// Excluye entradas propias, ocultas, no elegibles para feed, bloqueos y cuentas sin confirmar.
    /// La privacidad unificada permite amigos de cuentas públicas y privadas. Las amistades se consultan sin caché.
    /// Comparte timeline-read (60/minuto/usuario) con línea de tiempo y períodos.</remarks>
    /// <response code="400">invalid_cursor: cursor inválido, modificado o de otro contexto. invalid_filter: límite inválido.</response>
    [HttpGet]
    [ProducesResponseType(typeof(FeedPageDto), 200)]
    public async Task<IActionResult> List([FromQuery] FeedListRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var viewer) || viewer <= 0) return Challenge();
        foreach (var name in new[] { "cursor", "limit" })
            if (Request.Query.TryGetValue(name, out var values) && values.Count != 1)
                throw new TimelineException(name == "cursor" ? "invalid_cursor" : "invalid_filter");
        return Ok(await feed.ListAsync(viewer, request, ct));
    }
}
