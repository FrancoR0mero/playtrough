using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Authorize, Route("api/groups/{id:int}"), Tags("Groups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
public sealed class GroupMembershipController(GroupMembershipService membership) : ControllerBase
{
    /// <summary>Unirse a un grupo público o solicitar acceso a uno privado.</summary>
    [HttpPut("membership")]
    [ProducesResponseType(typeof(GroupMembershipDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Join(int id, CancellationToken ct)
        => Respond(async actor => Ok(await membership.JoinAsync(id, actor, ct)));

    /// <summary>Salir del grupo o cancelar una solicitud pendiente.</summary>
    [HttpDelete("membership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Leave(int id, CancellationToken ct)
        => Respond(async actor =>
        {
            await membership.LeaveAsync(id, actor, ct);
            return NoContent();
        });

    /// <summary>Consultar los miembros visibles de un grupo al que pertenecés.</summary>
    [HttpGet("members")]
    [ProducesResponseType(typeof(GroupPeoplePageDto<GroupMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Members(int id, [FromQuery] int? cursor, [FromQuery] int limit = 20, CancellationToken ct = default)
        => Respond(async actor => HasValidPage(cursor, limit)
            ? Ok(await membership.ListMembersAsync(id, actor, cursor ?? 0, limit, ct))
            : ValidationProblem(ModelState));

    /// <summary>Consultar solicitudes pendientes como propietario del grupo.</summary>
    [HttpGet("join-requests")]
    [ProducesResponseType(typeof(GroupPeoplePageDto<GroupJoinRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> JoinRequests(int id, [FromQuery] int? cursor, [FromQuery] int limit = 20, CancellationToken ct = default)
        => Respond(async actor => HasValidPage(cursor, limit)
            ? Ok(await membership.ListRequestsAsync(id, actor, cursor ?? 0, limit, ct))
            : ValidationProblem(ModelState));

    /// <summary>Aprobar o rechazar una solicitud como propietario del grupo.</summary>
    [HttpPost("join-requests/{requestId:int}/decision")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Resolve(int id, int requestId, GroupJoinDecisionRequest request, CancellationToken ct)
        => Respond(async actor =>
        {
            if (request.Decision is not ("approve" or "reject"))
            {
                ModelState.AddModelError("decision", "Elegí aprobar o rechazar la solicitud.");
                return ValidationProblem(ModelState);
            }
            await membership.ResolveAsync(id, requestId, actor, request.Decision == "approve", ct);
            return NoContent();
        });

    /// <summary>Expulsar un miembro del grupo como propietario.</summary>
    [HttpDelete("members/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Remove(int id, int userId, CancellationToken ct)
        => Respond(async actor =>
        {
            await membership.RemoveAsync(id, userId, actor, ct);
            return NoContent();
        });

    private bool HasValidPage(int? cursor, int limit)
    {
        if (cursor is <= 0) ModelState.AddModelError("cursor", "El cursor debe ser positivo.");
        if (limit is < 1 or > 50) ModelState.AddModelError("limit", "El límite debe estar entre 1 y 50.");
        return ModelState.IsValid;
    }

    private async Task<IActionResult> Respond(Func<int, Task<IActionResult>> action)
    {
        if (!int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var actor) || actor <= 0) return Challenge();
        try
        {
            return await action(actor);
        }
        catch (GroupMembershipException error)
        {
            return Problem(statusCode: error.Status, detail: error.Message,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }
    }
}
