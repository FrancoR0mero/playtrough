namespace Playtrough.Api.Dtos.Games;

public sealed record GameSummaryDto(int ExternalId, string Name, string Slug, string? CoverUrl,
    int? ReleaseYear, IReadOnlyList<string> Platforms);

public sealed record GameSearchResponseDto(IReadOnlyList<GameSummaryDto> Items, int Page, int PageSize, bool HasMore, bool IsLimited = false);

public sealed record GameDetailsDto(int Id, int ExternalId, string Name, string Slug, string Summary,
    string? CoverUrl, string? CoverUrlLarge, DateOnly? ReleaseDate, IReadOnlyList<string> Platforms,
    IReadOnlyList<string> Genres, IReadOnlyList<string> Developers);
