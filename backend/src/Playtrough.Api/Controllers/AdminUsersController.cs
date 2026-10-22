using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Authorization;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

/// <summary>Administración de usuarios, roles y auditoría.</summary>
[ApiController]
[Route("api/admin")]
[Tags("Admin")]
[Authorize(Policy = AppPolicies.RequireAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class AdminUsersController(IAdminUserService service) : ControllerBase
{
    /// <summary>Listar usuarios con paginación y filtros.</summary>
    /// <remarks>Requiere rol Admin. Filtra por texto y rol; page comienza en 1 y pageSize admite de 1 a 50.</remarks>
    /// <response code="200">Página de usuarios, total de resultados y paginación.</response>
    /// <response code="400">Paginación o filtros inválidos.</response>
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResponse<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListUsers([FromQuery] UserListRequest request, CancellationToken ct)
        => Ok(await service.ListUsersAsync(request, ct));

    /// <summary>Consultar la auditoría de cambios de rol.</summary>
    /// <remarks>Requiere rol Admin. Permite paginar y filtrar por targetUserId.</remarks>
    /// <response code="200">Página de cambios de rol y su autor.</response>
    /// <response code="400">Paginación o targetUserId inválidos.</response>
    [HttpGet("role-changes")]
    [ProducesResponseType(typeof(PagedResponse<RoleChangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListRoleChanges([FromQuery] RoleChangeListRequest request, CancellationToken ct)
        => Ok(await service.ListRoleChangesAsync(request, ct));

    /// <summary>Cambiar el rol de un usuario.</summary>
    /// <remarks>Requiere rol Admin. Roles permitidos: User, Moderator y Admin. El cambio se audita y revoca los refresh tokens del usuario.</remarks>
    /// <response code="200">Usuario con su rol actual, incluso si el rol solicitado ya estaba asignado.</response>
    /// <response code="400">Datos inválidos. extensions.code puede ser invalid_role, cannot_change_own_role, last_admin o invalid_role_state.</response>
    /// <response code="404">Usuario inexistente; extensions.code: user_not_found.</response>
    /// <response code="500">No se pudo cambiar el rol; extensions.code: role_change_failed. Una excepción no controlada usa internal_error.</response>
    [HttpPut("users/{id:int}/role")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> ChangeRole([Range(1, int.MaxValue)] int id, [FromBody] ChangeRoleRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var actorId) || actorId <= 0)
            return Challenge();
        var result = await service.ChangeRoleAsync(actorId, id, request.Role, ct);
        if (result.Error is null) return Ok(result.User);
        var (status, code, detail) = result.Error switch
        {
            RoleChangeError.InvalidRole => (400, "invalid_role", "The requested role is invalid."),
            RoleChangeError.NotFound => (404, "user_not_found", "The user was not found."),
            RoleChangeError.SelfRoleChange => (400, "cannot_change_own_role", "You cannot change your own role."),
            RoleChangeError.LastAdmin => (400, "last_admin", "The last admin cannot be demoted."),
            RoleChangeError.InvalidRoleState => (400, "invalid_role_state", "The user must have exactly one supported role."),
            _ => (500, "role_change_failed", "The role could not be changed."),
        };
        return Problem(statusCode: status, title: "Role change failed", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
