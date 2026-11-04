using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Api.Dtos.Reviews;

public sealed class ReviewListRequest : IValidatableObject
{
    [FromQuery(Name = "sort")] public string? Sort { get; init; } = "recent";
    [FromQuery(Name = "page"), Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 30)] public int PageSize { get; init; } = 10;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not ("recent" or "top" or "low"))
            yield return new("El orden debe ser recent, top o low.", [nameof(Sort)]);
    }
}
