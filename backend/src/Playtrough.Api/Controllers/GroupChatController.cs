using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Hubs;

namespace Playtrough.Api.Controllers;

[ApiController, Authorize, Route("api/groups/{id:int}/messages"), Tags("Groups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
public sealed class GroupChatController(ChatHistoryService history) : ControllerBase
{
    /// <summary>Consultar mensajes del grupo si la membresía sigue vigente.</summary>
    /// <remarks>Devuelve mensajes visibles ordenados del más antiguo al más reciente. Usá before para
    /// cargar anteriores o after para recuperar posteriores. After admite 0 como punto de inicio. En las páginas
    /// de recuperación conservá el through de la primera respuesta y avanzá con nextCursor.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ChatHistoryPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChatHistoryPageDto>> List(int id, [FromQuery] long? before, [FromQuery] long? after,
        [FromQuery] long? through, [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        var subject = JwtHubUserIdProvider.GetUserId(User);
        if (subject is null) return Challenge();
        if (before is <= 0 || after is < 0 || (before.HasValue && after.HasValue))
            ModelState.AddModelError("cursor", "Indicá un solo cursor válido.");
        if (through.HasValue && (!after.HasValue || through.Value < after.Value))
            ModelState.AddModelError("through", "El límite de recuperación debe ser igual o mayor al cursor posterior.");
        if (limit is < 1 or > 100) ModelState.AddModelError("limit", "El límite debe estar entre 1 y 100.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var viewer = int.Parse(subject, CultureInfo.InvariantCulture);
        var page = await history.ListAsync(id, viewer, before, after, through, limit, ct);
        return page is null ? NotFound() : page;
    }
}
