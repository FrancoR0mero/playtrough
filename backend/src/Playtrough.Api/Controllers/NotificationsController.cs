using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Dtos.Notifications;
using Playtrough.Api.Hubs;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Authorize, Route("api/notifications"), Tags("Notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class NotificationsController(NotificationCenter center) : ControllerBase
{
    /// <summary>Consultar las notificaciones de la cuenta actual.</summary>
    /// <remarks>El cursor es opaco. Conservá el readCutoff de la primera página para la lectura masiva.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationPageDto>> List([FromQuery] string? cursor, [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (UserId() is not { } user) return Challenge();
        if (limit is < 1 or > 50) return Invalid("limit", "El límite debe estar entre 1 y 50.");
        if (await center.ListAsync(user, cursor, limit, ct) is not { } page)
            return Invalid("cursor", "El cursor no es válido.");
        return page;
    }

    /// <summary>Consultar la cantidad de notificaciones sin leer.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(NotificationCountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationCountDto>> Count(CancellationToken ct)
        => UserId() is { } user ? await center.CountAsync(user, ct) : Challenge();

    /// <summary>Marcar una notificación propia como leída.</summary>
    [HttpPut("{id:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
    {
        if (UserId() is not { } user) return Challenge();
        return id > 0 && await center.ReadAsync(user, id, ct) ? NoContent() : NotFound();
    }

    /// <summary>Marcar como leídas las notificaciones propias hasta el corte consultado.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(typeof(NotificationCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationCountDto>> ReadAll(ReadNotificationsRequest request, CancellationToken ct)
    {
        if (UserId() is not { } user) return Challenge();
        if (await center.ReadAllAsync(user, request.Cutoff, ct) is not { } count)
            return Invalid("cutoff", "Volvé a consultar la lista para obtener un corte válido.");
        return count;
    }

    private int? UserId() => JwtHubUserIdProvider.GetUserId(User) is { } id ? int.Parse(id, CultureInfo.InvariantCulture) : null;

    private ActionResult Invalid(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
