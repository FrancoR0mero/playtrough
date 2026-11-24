using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/timeline"), Tags("Comments"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class TimelineCommentsController(TimelineCommentService comments) : ControllerBase
{
    /// <summary>Publicar un comentario en una entrada visible, incluso propia.</summary>
    /// <remarks>Texto plano de 1 a 500 caracteres después de normalizarlo. Política comments-write: 20/minuto/usuario.</remarks>
    /// <response code="400">invalid_comment: el texto normalizado está vacío, supera los 500 caracteres o el cuerpo es inválido.</response>
    /// <response code="404">entry_not_found: entrada inexistente o no visible.</response>
    /// <response code="409">duplicate_comment: el mismo autor publicó el mismo texto en esta entrada en los últimos 10 segundos.</response>
    [HttpPost("entries/{id:long}/comments")]
    [EnableRateLimiting(RateLimitPolicies.CommentsWrite)]
    [ProducesResponseType(typeof(CommentCreatedDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Add(long id, [FromBody] SaveCommentRequest request, CancellationToken ct)
        => UserId() is { } actor ? StatusCode(201, await comments.AddAsync(actor, id, request.Text, ct)) : Challenge();

    /// <summary>Consultar los comentarios de una entrada visible.</summary>
    /// <remarks>Orden descendente por fecha de publicación e id. limit: 20 por defecto, máximo 50.
    /// Usá nextCursor para continuar. Comparte la política timeline-read.</remarks>
    /// <response code="400">invalid_filter: límite inválido. invalid_cursor: cursor inválido o de otro contexto.</response>
    /// <response code="404">entry_not_found: entrada inexistente o no visible.</response>
    [HttpGet("entries/{id:long}/comments")]
    [EnableRateLimiting(RateLimitPolicies.TimelineRead)]
    [ProducesResponseType(typeof(CommentPageDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> List(long id, [FromQuery] TimelineInteractionPageRequest request, CancellationToken ct)
    {
        if (UserId() is not { } viewer) return Challenge();
        foreach (var name in new[] { "cursor", "limit" })
            if (Request.Query.TryGetValue(name, out var values) && values.Count != 1)
                throw new TimelineException(name == "cursor" ? "invalid_cursor" : "invalid_filter");
        return Ok(await comments.ListAsync(viewer, id, request, ct));
    }

    /// <summary>Editar un comentario propio de una entrada visible.</summary>
    /// <remarks>editedAt se actualiza cuando cambia el texto normalizado. Comparte comments-write.</remarks>
    /// <response code="400">invalid_comment: el texto normalizado está vacío, supera los 500 caracteres o el cuerpo es inválido.</response>
    /// <response code="404">comment_not_found: comentario inexistente, ajeno o en una entrada no visible.</response>
    [HttpPut("comments/{id:long}")]
    [EnableRateLimiting(RateLimitPolicies.CommentsWrite)]
    [ProducesResponseType(typeof(CommentDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Edit(long id, [FromBody] SaveCommentRequest request, CancellationToken ct)
        => UserId() is { } actor ? Ok(await comments.EditAsync(actor, id, request.Text, ct)) : Challenge();

    /// <summary>Borrar un comentario propio o un comentario de una entrada propia.</summary>
    /// <remarks>El borrado es definitivo. Comparte comments-write.</remarks>
    /// <response code="404">comment_not_found: comentario inexistente o sin permiso para borrarlo.</response>
    [HttpDelete("comments/{id:long}")]
    [EnableRateLimiting(RateLimitPolicies.CommentsWrite)]
    [ProducesResponseType(typeof(CommentCountDto), 200)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => UserId() is { } actor ? Ok(await comments.DeleteAsync(actor, id, ct)) : Challenge();

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
