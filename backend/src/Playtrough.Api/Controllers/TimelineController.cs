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

[ApiController, Route("api/timeline"), Tags("Timeline"), Authorize]
[EnableRateLimiting(RateLimitPolicies.TimelineRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class TimelineController(ITimelineService timeline) : ControllerBase
{
    /// <summary>Consultar la línea propia, incluidas las entradas ocultas.</summary>
    /// <remarks>Orden: occurredOn, recordedAt e id descendentes. limit: 20 por defecto, máximo 50.
    /// year: 1970 hasta año UTC actual + 1. month: 1–12, con o sin year.
    /// type: added, status_changed, reviewed, separados por coma. Filtros combinables.
    /// Cursor opaco v1, protegido y vinculado al usuario y filtros. No modificarlo.
    /// Errores: invalid_filter o invalid_cursor (400). Política timeline-read: 60/minuto/usuario.</remarks>
    [HttpGet("me")]
    [ProducesResponseType(typeof(TimelinePageDto), 200)]
    public Task<IActionResult> Mine([FromQuery] TimelineListRequest request, CancellationToken ct) => List(null, request, ct);

    /// <summary>Consultar los años y meses con actividad propia, incluyendo entradas ocultas.</summary>
    /// <remarks>type opcional: added, status_changed, reviewed, separados por coma.
    /// Conteos por fecha efectiva, años y meses descendentes. invalid_filter: 400.
    /// Comparte la política timeline-read con los listados.</remarks>
    [HttpGet("me/periods")]
    [ProducesResponseType(typeof(TimelinePeriodsDto), 200)]
    public Task<IActionResult> MinePeriods([FromQuery] string? type, CancellationToken ct) => Periods(null, type, ct);

    /// <summary>Consultar la línea de un usuario confirmado, según la privacidad de su cuenta.</summary>
    /// <remarks>Mismos filtros, cursor y orden que /api/timeline/me. Excluye entradas ocultas,
    /// salvo que el visitante sea el dueño. Público permite usuarios autenticados; Privado permite dueño y amigos.
    /// history_restricted: 403 con restriction=private. user_not_found: 404 si no existe, no confirmó su cuenta
    /// o existe un bloqueo. invalid_filter o invalid_cursor: 400. Política timeline-read compartida.</remarks>
    [HttpGet("/api/users/{username:regex(^(?!(?i:me|search)$).+$)}/timeline")]
    [ProducesResponseType(typeof(TimelinePageDto), 200)]
    public Task<IActionResult> UserTimeline(string username, [FromQuery] TimelineListRequest request, CancellationToken ct) => List(username, request, ct);

    /// <summary>Consultar los períodos visibles de la línea de un usuario.</summary>
    /// <remarks>Respeta la misma privacidad y entradas ocultas que su línea. type opcional permite
    /// los mismos valores separados por coma. history_restricted: 403 con restriction=private;
    /// user_not_found: 404; invalid_filter: 400. Política timeline-read compartida.</remarks>
    [HttpGet("/api/users/{username:regex(^(?!(?i:me|search)$).+$)}/timeline/periods")]
    [ProducesResponseType(typeof(TimelinePeriodsDto), 200)]
    public Task<IActionResult> UserPeriods(string username, [FromQuery] string? type, CancellationToken ct) => Periods(username, type, ct);

    private async Task<IActionResult> List(string? username, TimelineListRequest request, CancellationToken ct)
    {
        if (UserId() is not { } viewer) return Challenge();
        CheckRepeatedParameters();
        return Ok(await timeline.ListAsync(viewer, username, request, ct));
    }

    private async Task<IActionResult> Periods(string? username, string? type, CancellationToken ct)
    {
        if (UserId() is not { } viewer) return Challenge();
        CheckRepeatedParameters();
        if (Request.Query.ContainsKey("type") && string.IsNullOrWhiteSpace(type)) throw new TimelineException("invalid_filter");
        return Ok(await timeline.PeriodsAsync(viewer, username, type, ct));
    }

    private void CheckRepeatedParameters()
    {
        foreach (var name in new[] { "cursor", "limit", "year", "month", "type" })
            if (Request.Query.TryGetValue(name, out var values) && values.Count != 1)
                throw new TimelineException(name == "cursor" ? "invalid_cursor" : "invalid_filter");
    }

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
