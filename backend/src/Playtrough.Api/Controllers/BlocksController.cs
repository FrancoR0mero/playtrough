using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Blocks;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/blocks"), Tags("Users"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class BlocksController(IBlockService blocks) : ControllerBase
{
    /// <summary>Bloquear una cuenta y eliminar la amistad y solicitudes pendientes entre ambos.</summary>
    /// <remarks>Idempotente. No permite autobloqueo. Comparte friends-write; no genera notificaciones.</remarks>
    [HttpPost, EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Block(BlockUserRequest request, CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        if (request.Username.ValueKind != JsonValueKind.String) throw new ProfileException("invalid_query");
        await blocks.BlockAsync(id, request.Username.GetString()!, ct);
        return NoContent();
    }

    /// <summary>Eliminar únicamente un bloqueo propio, sin restaurar amistades ni solicitudes.</summary>
    /// <remarks>Idempotente, incluso si no existe la cuenta. Un bloqueo en sentido contrario continúa vigente.</remarks>
    [HttpDelete("{username}"), EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Unblock(string username, CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        await blocks.UnblockAsync(id, username, ct);
        return NoContent();
    }

    /// <summary>Listar solo los bloqueos realizados por el usuario autenticado.</summary>
    /// <remarks>Orden alfabético; page=1, pageSize=20, máximo 50. No expone email ni ids internos.</remarks>
    [HttpGet, ProducesResponseType(typeof(BlockedUsersPage), 200)]
    public async Task<IActionResult> List([FromQuery] FriendsPageRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await blocks.ListAsync(id, request, ct)) : Challenge();

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
