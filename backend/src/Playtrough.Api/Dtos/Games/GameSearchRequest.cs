using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Api.Dtos.Games;

public sealed class GameSearchRequest : IValidatableObject
{
    /// <summary>Texto obligatorio de 2 a 100 caracteres después de recortar espacios.</summary>
    [FromQuery(Name = "q")] public string? Q { get; init; }
    [FromQuery(Name = "page"), Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 40)] public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Q is null || Q.Trim().Length is < 2 or > 100)
            yield return new ValidationResult("La consulta debe tener entre 2 y 100 caracteres después de recortar espacios.", [nameof(Q)]);
    }
}
