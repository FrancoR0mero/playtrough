using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/users"), Tags("Users"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class UsersController(IProfileService profiles, IUserSearchService search, IPrivacyService privacy) : ControllerBase
{
    /// <summary>Buscar usuarios confirmados por username, sin distinguir mayúsculas.</summary>
    /// <remarks>q es obligatorio: 2–50 caracteres Unicode tras recortar. % y _ son literales.
    /// Excluye al usuario del token. Orden: exacto, prefijo, resto; desempate por username normalizado e id.
    /// page=1, pageSize=20 (máximo 20), hasMore. Solo expone username, avatarUrl y relationship.
    /// Política user-search: 30 consultas por minuto por usuario.</remarks>
    /// <response code="400">invalid_query: q o paginación inválidos.</response>
    [HttpGet("search"), EnableRateLimiting(RateLimitPolicies.UserSearch)]
    [ProducesResponseType(typeof(UserSearchResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> Search([FromQuery] UserSearchRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await search.SearchAsync(id, request, ct)) : Challenge();

    /// <summary>Consultar el perfil propio, incluido el email y la próxima fecha de cambio de username.</summary>
    /// <response code="404">user_not_found: el usuario del token ya no existe.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(MyProfileDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => UserId() is { } id ? Ok(await profiles.GetMineAsync(id, ct)) : Challenge();

    /// <summary>Consultar un perfil por username sin distinguir mayúsculas. Nunca expone email.</summary>
    /// <remarks>Si está restringido devuelve 200 con username, avatarUrl, relationship, isRestricted=true
    /// y restriction=private o friends_only. Omite bio, createdAt y friendsCount. El dueño siempre puede verlo.</remarks>
    /// <response code="404">user_not_found: no existe o no confirmó su email (salvo el dueño).</response>
    [HttpGet("{username:regex(^(?!(?i:me|search)$).+$)}")]
    [ProducesResponseType(typeof(ProfileDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Get(string username, CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        var profile = await profiles.GetAsync(id, username, ct);
        return profile.IsRestricted
            ? Ok(new RestrictedProfileDto(profile.Username, profile.AvatarUrl, true, profile.Restriction!, profile.Relationship))
            : Ok(profile);
    }

    /// <summary>Consultar la configuración propia de privacidad de perfil e historial.</summary>
    /// <remarks>Valores: public, friends, private. Historial controla la biblioteca; las reseñas de juegos siguen públicas.</remarks>
    [HttpGet("me/privacy")]
    [ProducesResponseType(typeof(PrivacyDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetPrivacy(CancellationToken ct)
        => UserId() is { } id ? Ok(await privacy.GetAsync(id, ct)) : Challenge();

    /// <summary>Actualizar la privacidad propia de perfil e historial.</summary>
    /// <remarks>Ambos campos son obligatorios y aceptan public, friends o private. Efecto inmediato.
    /// Comparte el límite profile-write con la edición del perfil (10 por minuto por usuario).</remarks>
    /// <response code="400">invalid_visibility: valor inválido, campo ausente o cuerpo inválido.</response>
    [HttpPut("me/privacy"), EnableRateLimiting(RateLimitPolicies.ProfileWrite)]
    [ProducesResponseType(typeof(PrivacyDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> UpdatePrivacy(UpdatePrivacyRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await privacy.UpdateAsync(id, request, ct)) : Challenge();

    /// <summary>Listar los amigos de un usuario si su perfil es visible para el visitante.</summary>
    /// <remarks>El dueño siempre tiene acceso. Orden alfabético e id; page=1, pageSize=20, máximo 50, hasMore.</remarks>
    /// <response code="400">invalid_query: paginación inválida.</response>
    /// <response code="403">profile_restricted: el perfil es privado o requiere amistad.</response>
    /// <response code="404">user_not_found: cuenta inexistente o no confirmada (salvo el dueño).</response>
    [HttpGet("{username:regex(^(?!(?i:me|search)$).+$)}/friends")]
    [ProducesResponseType(typeof(FriendsPage<FriendDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Friends(string username, [FromQuery] FriendsPageRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await profiles.FriendsAsync(id, username, request, ct)) : Challenge();

    /// <summary>Consultar la biblioteca y estados de juegos según la privacidad del historial.</summary>
    /// <remarks>Solo lectura. La visibilidad del historial es independiente de la del perfil.
    /// El dueño siempre puede acceder. Admite los filtros y paginación de la biblioteca propia.</remarks>
    [HttpGet("{username:regex(^(?!(?i:me|search)$).+$)}/library")]
    [ProducesResponseType(typeof(LibraryListResponseDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Library(string username, [FromQuery] LibraryListRequest request, CancellationToken ct)
        => UserId() is { } id ? Ok(await profiles.LibraryAsync(id, username, request, ct)) : Challenge();

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
