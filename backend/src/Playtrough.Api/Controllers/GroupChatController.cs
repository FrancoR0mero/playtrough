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
public sealed class GroupChatController(ChatHistoryService history, ChatMessageService messages) : ControllerBase
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
        if (ViewerId() is not { } viewer) return Challenge();
        if (before is <= 0 || after is < 0 || (before.HasValue && after.HasValue))
            ModelState.AddModelError("cursor", "Indicá un solo cursor válido.");
        if (through.HasValue && (!after.HasValue || through.Value < after.Value))
            ModelState.AddModelError("through", "El límite de recuperación debe ser igual o mayor al cursor posterior.");
        if (limit is < 1 or > 100) ModelState.AddModelError("limit", "El límite debe estar entre 1 y 100.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var page = await history.ListAsync(id, viewer, before, after, through, limit, ct);
        return page is null ? NotFound() : page;
    }

    /// <summary>Verificar el estado y la visibilidad actual de mensajes ya cargados.</summary>
    /// <remarks>Enviá hasta 100 IDs mediante parámetros ids repetidos. Los mensajes visibles devuelven
    /// su ID y fecha de retirada si corresponde. Los mensajes inaccesibles se omiten.</remarks>
    [HttpGet("state")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageStateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageStateDto>>> State(int id, [FromQuery] long[] ids, CancellationToken ct)
    {
        if (ViewerId() is not { } viewer) return Challenge();
        if (ids.Length is < 1 or > 100 || ids.Any(value => value <= 0))
        {
            ModelState.AddModelError("ids", "Indicá entre 1 y 100 IDs de mensajes válidos.");
            return ValidationProblem(ModelState);
        }
        var result = await history.ReadStateAsync(id, viewer, ids, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Retirar un mensaje propio o retirar un mensaje del grupo como propietario.</summary>
    /// <remarks>Requiere membresía vigente. Las solicitudes repetidas devuelven el mismo marcador de retirada.</remarks>
    [HttpDelete("{messageId:long}")]
    [ProducesResponseType(typeof(ChatMessageStateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatMessageStateDto>> Remove(int id, long messageId, CancellationToken ct)
    {
        if (ViewerId() is not { } actor) return Challenge();
        var state = await messages.RemoveAsync(id, messageId, actor, ct);
        return state is null ? NotFound() : state;
    }

    private int? ViewerId() => JwtHubUserIdProvider.GetUserId(User) is { } subject
        ? int.Parse(subject, CultureInfo.InvariantCulture) : null;
}
