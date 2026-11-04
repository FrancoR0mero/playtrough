namespace Playtrough.Api.Services.Contracts;

public record GameSummary(int ExternalId, string Name, string Slug, string? CoverUrl,
    DateOnly? ReleaseDate, IReadOnlyList<string> Platforms);

public sealed record GameDetails(int ExternalId, string Name, string Slug, string? CoverUrl,
    DateOnly? ReleaseDate, IReadOnlyList<string> Platforms, string Summary,
    IReadOnlyList<string> Genres, IReadOnlyList<string> Developers, string? CoverUrlLarge)
    : GameSummary(ExternalId, Name, Slug, CoverUrl, ReleaseDate, Platforms);

public sealed record GameSearchResult(IReadOnlyList<GameSummary> Items, int Page, int PageSize, bool HasMore, bool IsLimited = false);
