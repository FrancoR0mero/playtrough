using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Api.Dtos.Library;

public sealed record LibraryEntryDto(int GameId, int ExternalId, string Name, string Slug,
    string? CoverUrl, int? ReleaseYear, string Status, DateOnly StatusChangedOn, DateTime AddedAt);

public sealed record LibraryCountsDto(int Playing, int Backlog, int Beaten, int Completed, int Abandoned)
{
    public int Total => Playing + Backlog + Beaten + Completed + Abandoned;
}

public sealed record LibraryListResponseDto(IReadOnlyList<LibraryEntryDto> Items, LibraryCountsDto Counts,
    int Page, int PageSize, bool HasMore);

public class LibraryStatusRequest
{
    [Required] public string? Status { get; init; }
    public DateOnly? OccurredOn { get; init; }
}

public sealed class AddLibraryRequest : LibraryStatusRequest
{
    public int GameId { get; init; }
}

public sealed class LibraryListRequest : IValidatableObject
{
    [FromQuery(Name = "status")] public string? Status { get; init; }
    [FromQuery(Name = "q")] public string? Q { get; init; }
    [FromQuery(Name = "sort")] public string? Sort { get; init; } = "recent";
    [FromQuery(Name = "page"), Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 40)] public int PageSize { get; init; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Status) && Status is not ("playing" or "backlog" or "beaten" or "completed" or "abandoned"))
            yield return new("El estado de la consulta no es válido.", [nameof(Status)]);
        if (!string.IsNullOrEmpty(Sort) && Sort is not ("recent" or "added" or "name" or "release"))
            yield return new("El orden no es válido.", [nameof(Sort)]);
        if (Q is not null && (Q.Trim().Length > 100 || Q.Any(char.IsControl)))
            yield return new("La búsqueda admite hasta 100 caracteres sin caracteres de control.", [nameof(Q)]);
    }
}
