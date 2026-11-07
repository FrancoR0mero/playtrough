using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/users"), Tags("Users"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class UsersController(IProfileService profiles) : ControllerBase
{
    /// <summary>Consultar el perfil propio, incluido el email y la próxima fecha de cambio de username.</summary>
    /// <response code="404">user_not_found: el usuario del token ya no existe.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(MyProfileDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => UserId() is { } id ? Ok(await profiles.GetMineAsync(id, ct)) : Challenge();

    /// <summary>Consultar un perfil por username sin distinguir mayúsculas. Nunca expone email.</summary>
    /// <response code="404">user_not_found: no existe o no confirmó su email (salvo el dueño).</response>
    [HttpGet("{username:regex(^(?!(?i:me|search)$).+$)}")]
    [ProducesResponseType(typeof(ProfileDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Get(string username, CancellationToken ct)
        => UserId() is { } id ? Ok(await profiles.GetAsync(id, username, ct)) : Challenge();

    /// <summary>Editar únicamente los campos presentes del perfil propio.</summary>
    /// <remarks>username: 3–20 letras ASCII, números o guion bajo. Un cambio cada 30 días por defecto.
    /// El mismo nombre sin distinguir mayúsculas no cambia nada. bio: null o hasta 300 caracteres Unicode
    /// después del saneamiento. Presupuesto profile-write: 10 por minuto por usuario.
    /// El frontend debe renovar el JWT tras cambiar el username.</remarks>
    /// <response code="400">invalid_username, invalid_bio, username_reserved.</response>
    /// <response code="409">username_taken, username_change_too_soon (availableAt UTC), concurrent_modification.</response>
    /// <response code="429">rate_limited, con Retry-After.</response>
    [HttpPut("me"), EnableRateLimiting(RateLimitPolicies.ProfileWrite)]
    [ProducesResponseType(typeof(MyProfileDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> Update(UpdateProfileRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await profiles.UpdateAsync(id, request, ct)) : Challenge();

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
