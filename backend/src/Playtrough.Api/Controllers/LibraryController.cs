using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

/// <summary>Biblioteca privada del usuario autenticado. Los IDs de juego son locales.</summary>
[ApiController]
[Route("api/library")]
[Tags("Library")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class LibraryController(ILibraryService library) : ControllerBase
{
    /// <summary>Listar la biblioteca propia con contadores globales y paginación.</summary>
    /// <remarks>status es opcional. q admite hasta 100 caracteres; page empieza en 1 y pageSize admite 1–40.
    /// recent ordena por fecha efectiva del estado, added por fecha de alta, name por nombre ascendente
    /// y release por lanzamiento descendente. Todos incluyen desempate por ID local.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(LibraryListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] LibraryListRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        return Ok(await library.ListAsync(userId, request, ct));
    }

    /// <summary>Consultar una entrada propia; not_in_library indica ausencia normal.</summary>
    [HttpGet("games/{gameId}")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEntry(int gameId, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        var entry = await library.GetEntryAsync(userId, gameId, ct) ?? throw new LibraryException("not_in_library");
        return Ok(await library.DescribeAsync(entry, ct));
    }

    /// <summary>Agregar un juego local a la biblioteca propia.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add([FromBody] AddLibraryRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        var entry = await library.AddAsync(userId, request.GameId, LibraryValues.ParseStatus(request.Status), request.OccurredOn, ct);
        return CreatedAtAction(nameof(GetEntry), new { gameId = entry.GameId }, await library.DescribeAsync(entry, ct));
    }

    /// <summary>Cambiar el estado; repetir el mismo estado devuelve la entrada sin modificarla.</summary>
    [HttpPut("{gameId}/status")]
    [ProducesResponseType(typeof(LibraryEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(int gameId, [FromBody] LibraryStatusRequest request, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        var entry = await library.ChangeStatusAsync(userId, gameId, LibraryValues.ParseStatus(request.Status), request.OccurredOn, ct);
        return Ok(await library.DescribeAsync(entry, ct));
    }

    /// <summary>Quitar una entrada propia, conservando los eventos internos existentes.</summary>
    [HttpDelete("{gameId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(int gameId, CancellationToken ct)
    {
        if (UserId() is not { } userId) return Challenge();
        await library.RemoveAsync(userId, gameId, ct);
        return NoContent();
    }

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
