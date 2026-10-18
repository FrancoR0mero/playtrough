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

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AppPolicies.RequireAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUsersController(IAdminUserService service) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers([FromQuery] UserListRequest request, CancellationToken ct)
        => Ok(await service.ListUsersAsync(request, ct));

    [HttpGet("role-changes")]
    public async Task<IActionResult> ListRoleChanges([FromQuery] RoleChangeListRequest request, CancellationToken ct)
        => Ok(await service.ListRoleChangesAsync(request, ct));

    [HttpPut("users/{id:int}/role")]
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
            RoleChangeError.SelfRoleChange => (400, "self_role_change", "You cannot change your own role."),
            RoleChangeError.LastAdmin => (400, "last_admin", "The last admin cannot be demoted."),
            RoleChangeError.InvalidRoleState => (400, "invalid_role_state", "The user must have exactly one supported role."),
            _ => (500, "role_change_failed", "The role could not be changed."),
        };
        return Problem(statusCode: status, title: "Role change failed", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
