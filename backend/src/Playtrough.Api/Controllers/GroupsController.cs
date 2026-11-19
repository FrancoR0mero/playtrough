using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Models;
using Playtrough.Api.Services;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Authorize, Route("api/groups"), Tags("Groups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class GroupsController(GroupService groups, IOptions<GroupSettings> settings) : ControllerBase
{
    /// <summary>Consultar las temáticas disponibles para grupos.</summary>
    [HttpGet("topics")]
    public ActionResult<string[]> Topics() => settings.Value.Topics;

    /// <summary>Descubrir grupos públicos y privados mediante su ficha básica.</summary>
    /// <remarks>Filtra por texto y temática. Usá nextCursor con los mismos filtros para continuar el listado.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GroupPageDto>> List([FromQuery] string? q, [FromQuery] string? topic,
        [FromQuery] int? cursor, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var text = PlainText.Sanitize(q);
        var selectedTopic = FindTopic(topic);
        if (text is { Length: > 100 }) ModelState.AddModelError("q", "La búsqueda admite hasta 100 caracteres.");
        if (topic is not null && selectedTopic is null) ModelState.AddModelError("topic", "Elegí una temática válida.");
        if (cursor is <= 0) ModelState.AddModelError("cursor", "El cursor debe ser positivo.");
        if (limit is < 1 or > 50) ModelState.AddModelError("limit", "El límite debe estar entre 1 y 50.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return await groups.ListAsync(text, selectedTopic, cursor, limit, ct);
    }

    /// <summary>Consultar la ficha de un grupo y la membresía propia.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupProfileDto>> Get(int id, CancellationToken ct)
    {
        if (UserId() is not { } viewer) return Challenge();
        return await groups.FindAsync(id, viewer, ct) is { } group ? group : NotFound();
    }

    /// <summary>Crear un grupo y registrar al usuario autenticado como propietario y miembro.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GroupDto>> Create(CreateGroupRequest request, CancellationToken ct)
    {
        if (UserId() is not { } ownerId) return Challenge();

        var name = PlainText.Sanitize(request.Name);
        var description = PlainText.Sanitize(request.Description);
        var topic = FindTopic(request.Topic);
        var privacy = request.Privacy switch
        {
            "public" => GroupPrivacy.Public,
            "private" => GroupPrivacy.Private,
            _ => (GroupPrivacy?)null,
        };
        if (name is not { Length: <= SocialGroup.MaxNameLength })
            ModelState.AddModelError("name", "Ingresá un nombre de hasta 80 caracteres.");
        if (description is not { Length: <= SocialGroup.MaxDescriptionLength })
            ModelState.AddModelError("description", "Ingresá una descripción de hasta 500 caracteres.");
        if (topic is null) ModelState.AddModelError("topic", "Elegí una temática válida.");
        if (privacy is null) ModelState.AddModelError("privacy", "Elegí público o privado.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var group = await groups.CreateAsync(ownerId, name!, description!, topic!, privacy!.Value, ct);
        return CreatedAtAction(nameof(Get), new { group.Id }, group);
    }

    private string? FindTopic(string? topic)
        => settings.Value.Topics.FirstOrDefault(value => string.Equals(value, topic?.Trim(), StringComparison.OrdinalIgnoreCase));

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
