using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/friends"), Tags("Friends"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class FriendsController(IFriendService friends) : ControllerBase
{
    /// <summary>Enviar una solicitud de amistad.</summary>
    /// <remarks>Solo el username del destinatario; el remitente siempre proviene del token.
    /// friends-write: 30 escrituras por minuto y usuario. LÃ­mite predeterminado: 50 pendientes enviadas.</remarks>
    /// <response code="400">cannot_befriend_self; invalid_query para un cuerpo invÃ¡lido.</response>
    /// <response code="404">user_not_found: no existe o no confirmÃ³ su email.</response>
    /// <response code="409">already_friends, request_already_sent, request_already_received (requestId), cooldown_active (availableAt UTC), too_many_pending_requests.</response>
    [HttpPost("requests"), EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(typeof(SentFriendRequestDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Send(SendFriendRequest request, CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        if (request.Username.ValueKind != JsonValueKind.String) throw new FriendException("invalid_query");
        var result = await friends.SendRequestAsync(id, request.Username.GetString()!, ct);
        return StatusCode(201, result);
    }

    /// <summary>Listar las solicitudes recibidas pendientes del usuario del token.</summary>
    /// <remarks>MÃ¡s recientes primero, desempate por id descendente. page=1, pageSize=20 (mÃ¡ximo 50), hasMore.</remarks>
    /// <response code="400">invalid_query: paginaciÃ³n invÃ¡lida.</response>
    [HttpGet("requests/received")]
    [ProducesResponseType(typeof(FriendsPage<FriendRequestDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Received([FromQuery] FriendsPageRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await friends.RequestsAsync(id, true, request, ct)) : Challenge();

    /// <summary>Listar las solicitudes enviadas pendientes del usuario del token.</summary>
    /// <remarks>MÃ¡s recientes primero, desempate por id descendente. page=1, pageSize=20 (mÃ¡ximo 50), hasMore.</remarks>
    /// <response code="400">invalid_query: paginaciÃ³n invÃ¡lida.</response>
    [HttpGet("requests/sent")]
    [ProducesResponseType(typeof(FriendsPage<FriendRequestDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Sent([FromQuery] FriendsPageRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await friends.RequestsAsync(id, false, request, ct)) : Challenge();

    /// <summary>Aceptar una solicitud recibida y crear una amistad mutua.</summary>
    /// <remarks>Idempotente si ya fue aceptada por el receptor. PolÃ­tica compartida friends-write.</remarks>
    /// <response code="404">request_not_found: inexistente o el usuario no es el receptor.</response>
    /// <response code="409">request_not_pending: rechazada o cancelada.</response>
    [HttpPost("requests/{id:int}/accept"), EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(typeof(AcceptedFriendDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Accept(int id, CancellationToken ct)
        => UserId() is { } user ? Ok(await friends.AcceptAsync(user, id, ct)) : Challenge();

    /// <summary>Rechazar una solicitud recibida sin notificar al remitente.</summary>
    /// <remarks>Idempotente si ya fue rechazada. Espera predeterminada de 7 dÃ­as para el mismo remitente. PolÃ­tica friends-write.</remarks>
    /// <response code="404">request_not_found: inexistente o el usuario no es el receptor.</response>
    /// <response code="409">request_not_pending: aceptada o cancelada.</response>
    [HttpPost("requests/{id:int}/reject"), EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Reject(int id, CancellationToken ct)
    {
        if (UserId() is not { } user) return Challenge();
        await friends.RejectAsync(user, id, ct);
        return NoContent();
    }

    /// <summary>Cancelar una solicitud enviada, sin generar espera.</summary>
    /// <remarks>Idempotente si ya fue cancelada. PolÃ­tica compartida friends-write.</remarks>
    /// <response code="404">request_not_found: inexistente o el usuario no es el remitente.</response>
    /// <response code="409">request_not_pending: aceptada o rechazada.</response>
    [HttpDelete("requests/{id:int}"), EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        if (UserId() is not { } user) return Challenge();
        await friends.CancelAsync(user, id, ct);
        return NoContent();
    }

    /// <summary>Listar los amigos propios, con bÃºsqueda opcional por username.</summary>
    /// <remarks>q es una subcadena literal sin distinguir mayÃºsculas. Orden alfabÃ©tico y desempate por id.
    /// page=1, pageSize=20 (mÃ¡ximo 50), hasMore. Incluye fecha de amistad.</remarks>
    /// <response code="400">invalid_query: paginaciÃ³n invÃ¡lida.</response>
    [HttpGet]
    [ProducesResponseType(typeof(FriendsPage<FriendDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] FriendsPageRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await friends.ListAsync(id, q, request, ct)) : Challenge();

    /// <summary>Consultar los contadores propios de amigos y solicitudes pendientes.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(FriendsSummaryDto), 200)]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => UserId() is { } id ? Ok(await friends.SummaryAsync(id, ct)) : Challenge();

    /// <summary>Eliminar una amistad mutua sin generar espera para nuevas solicitudes.</summary>
    /// <remarks>Idempotente si la cuenta existe y ya no eran amigos. PolÃ­tica compartida friends-write.</remarks>
    /// <response code="404">user_not_found: cuenta inexistente o sin confirmar.</response>
    [HttpDelete("{username}"), EnableRateLimiting(RateLimitPolicies.FriendsWrite)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Remove(string username, CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        await friends.RemoveFriendAsync(id, username, ct);
        return NoContent();
    }

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
