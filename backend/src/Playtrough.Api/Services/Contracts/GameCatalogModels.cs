namespace Playtrough.Api.Services.Contracts;

public record GameSummary(int ExternalId, string Name, string Slug, string? CoverUrl,
    DateOnly? ReleaseDate, IReadOnlyList<string> Platforms)
{
    public string? SourceUrl { get; init; }
    public int? ReleaseYear { get; init; }
    public decimal? ProviderRating { get; init; }
    public int? ProviderRatingCount { get; init; }
    public decimal? CriticScore { get; init; }
}

public sealed record GameDetails(int ExternalId, string Name, string Slug, string? CoverUrl,
    DateOnly? ReleaseDate, IReadOnlyList<string> Platforms, string Summary,
    IReadOnlyList<string> Genres, IReadOnlyList<string> Developers, string? CoverUrlLarge)
    : GameSummary(ExternalId, Name, Slug, CoverUrl, ReleaseDate, Platforms)
{
    public IReadOnlyList<string> Screenshots { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public decimal? PlaytimeHours { get; init; }
    public bool? AdultOnly { get; init; }
    public IReadOnlyList<GameOffer> Offers { get; init; } = [];
}

public sealed record GameOffer(string StoreName, decimal? Value, decimal? Initial, decimal? DiscountPercent, string? Currency);
public sealed record GameMatchRequest(int? SteamId, int? IgdbId, string? Name, int? Year);

public sealed record GameSearchResult(IReadOnlyList<GameSummary> Items, int Page, int PageSize, bool HasMore, bool IsLimited = false);
