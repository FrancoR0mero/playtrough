using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.Models;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

public sealed class AvatarUploadRequest { public IFormFile? File { get; set; } }
public sealed record AvatarDto(string? AvatarUrl);

[ApiController, Route("api/users/me/avatar"), Tags("Users"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
[ProducesResponseType(typeof(ProblemDetails), 413)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
public sealed class AvatarsController(IAvatarService avatars) : ControllerBase
{
    /// <summary>Subir o reemplazar el avatar propio: JPG, PNG o WebP, hasta 5 MB, 64–4096 px por lado.</summary>
    /// <remarks>Valida el contenido real; corrige orientación, recorta al centro y genera WebP de 256 px sin metadatos.</remarks>
    [HttpPost, Consumes("multipart/form-data"), AvatarUpload]
    [EnableRateLimiting(RateLimitPolicies.ProfileWrite)]
    [ProducesResponseType(typeof(AvatarDto), 200)]
    public async Task<IActionResult> Upload([FromForm] AvatarUploadRequest request, CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        if (request.File is null || Request.Form.Files.Count != 1) throw new ProfileException("invalid_avatar");
        return Ok(new AvatarDto(await avatars.UploadAsync(id, request.File, ct)));
    }

    /// <summary>Eliminar el avatar propio y volver a las iniciales. Operación idempotente.</summary>
    [HttpDelete, EnableRateLimiting(RateLimitPolicies.ProfileWrite)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        if (UserId() is not { } id) return Challenge();
        await avatars.DeleteAsync(id, ct);
        return NoContent();
    }

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}

[ApiController, Route("api/avatars"), Tags("Users")]
public sealed class AvatarFilesController(IAvatarStorage storage) : ControllerBase
{
    /// <summary>Obtener únicamente un avatar procesado por su clave aleatoria pública.</summary>
    [HttpGet("{fileName}"), AllowAnonymous, Produces("image/webp")]
    [ProducesResponseType(typeof(Stream), 200)]
    [ProducesResponseType(404)]
    public IActionResult Get(string fileName)
    {
        var stream = storage.OpenRead(fileName);
        if (stream is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(stream, "image/webp");
    }
}
