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
        if (!HasValidPage(cursor, limit)) return ValidationProblem(ModelState);
        return await groups.ListAsync(text, selectedTopic, cursor, limit, ct);
    }

    /// <summary>Consultar la ficha de un grupo y la membresía propia.</summary>
    /// <remarks>La respuesta para el propietario incluye ETag para editar o eliminar el grupo.</remarks>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupProfileDto>> Get(int id, CancellationToken ct)
    {
        if (UserId() is not { } viewer) return Challenge();
        if (await groups.FindAsync(id, viewer, ct) is not { } group) return NotFound();
        if (group.Version is { } version) Response.Headers.ETag = $"\"{version:D}\"";
        return group.Profile;
    }

    /// <summary>Consultar las reseñas visibles de los miembros actuales del grupo.</summary>
    /// <remarks>Requiere membresía vigente. Aplica la privacidad de la cuenta del autor y el bloqueo bilateral.
    /// Las reseñas se ordenan por ID descendente. Usá nextCursor para seguir paginando.</remarks>
    [HttpGet("{id:int}/reviews")]
    [ProducesResponseType(typeof(GroupReviewPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupReviewPageDto>> Reviews(int id, [FromQuery] int? cursor,
        [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        if (UserId() is not { } viewer) return Challenge();
        if (!HasValidPage(cursor, limit)) return ValidationProblem(ModelState);
        return await groups.ListReviewsAsync(id, viewer, cursor, limit, ct) is { } page ? page : NotFound();
    }

    /// <summary>Crear un grupo y registrar al usuario autenticado como propietario y miembro.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GroupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GroupDto>> Create(SaveGroupRequest request, CancellationToken ct)
    {
        if (UserId() is not { } ownerId) return Challenge();
        if (ValidateDraft(request) is not { } draft) return ValidationProblem(ModelState);

        var group = await groups.CreateAsync(ownerId, draft.Name, draft.Description, draft.Topic, draft.Privacy, ct);
        return CreatedAtAction(nameof(Get), new { group.Id }, group);
    }

    /// <summary>Editar la ficha y la privacidad del grupo como propietario.</summary>
    /// <remarks>Enviá en If-Match el ETag de la ficha. Las membresías vigentes se conservan.</remarks>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(int id, SaveGroupRequest request, CancellationToken ct)
    {
        if (UserId() is not { } owner) return Challenge();
        var current = await groups.FindOwnedAsync(id, owner, ct);
        if (current is null) return NotFound();
        if (ReadVersion(out var version) is { } error) return error;
        if (ValidateDraft(request, current.Topic) is not { } draft) return ValidationProblem(ModelState);
        if (await groups.UpdateAsync(id, owner, version, draft.Name, draft.Description, draft.Topic, draft.Privacy, ct))
            return NoContent();
        return await WriteConflict(id, owner, ct);
    }

    /// <summary>Eliminar el grupo como propietario y revocar el acceso a su ficha e interior.</summary>
    /// <remarks>Enviá en If-Match el ETag de la ficha. El grupo se retira mediante borrado lógico.</remarks>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (UserId() is not { } owner) return Challenge();
        if (await groups.FindOwnedAsync(id, owner, ct) is null) return NotFound();
        if (ReadVersion(out var version) is { } error) return error;
        if (await groups.DeleteAsync(id, owner, version, ct)) return NoContent();
        return await WriteConflict(id, owner, ct);
    }

    private (string Name, string Description, string Topic, GroupPrivacy Privacy)? ValidateDraft(
        SaveGroupRequest request, string? currentTopic = null)
    {
        var name = PlainText.Sanitize(request.Name);
        var description = PlainText.Sanitize(request.Description);
        var topic = FindTopic(request.Topic);
        if (topic is null && string.Equals(currentTopic, request.Topic?.Trim(), StringComparison.OrdinalIgnoreCase))
            topic = currentTopic;
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
        return ModelState.IsValid ? (name!, description!, topic!, privacy!.Value) : null;
    }

    private ObjectResult? ReadVersion(out Guid version)
    {
        version = default;
        var header = Request.Headers.IfMatch;
        if (header.Count == 0)
            return Problem(statusCode: 428, detail: "Volvé a cargar la ficha para obtener su versión.");
        var value = header.Count == 1 ? header[0]?.Trim() : null;
        if (value is { Length: 38 } && value[0] == '"' && value[^1] == '"' &&
            Guid.TryParseExact(value.AsSpan(1, 36), "D", out version)) return null;
        return Problem(statusCode: 400, detail: "If-Match debe contener el ETag de la ficha.");
    }

    private async Task<IActionResult> WriteConflict(int id, int owner, CancellationToken ct)
        => await groups.FindOwnedAsync(id, owner, ct) is null ? NotFound() :
            Problem(statusCode: 412, detail: "El grupo cambió. Volvé a cargarlo antes de continuar.",
                extensions: new Dictionary<string, object?> { ["code"] = "group_version_conflict" });

    private string? FindTopic(string? topic)
        => settings.Value.Topics.FirstOrDefault(value => string.Equals(value, topic?.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool HasValidPage(int? cursor, int limit)
    {
        if (cursor is <= 0) ModelState.AddModelError("cursor", "El cursor debe ser positivo.");
        if (limit is < 1 or > 50) ModelState.AddModelError("limit", "El límite debe estar entre 1 y 50.");
        return ModelState.IsValid;
    }

    private int? UserId() => int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
}
