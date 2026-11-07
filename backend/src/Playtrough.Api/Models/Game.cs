namespace Playtrough.Api.Models;

public sealed class Game
{
    public int Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int ExternalId { get; set; }
    public int? GameBrainId { get; set; }
    public int? SteamId { get; set; }
    public int? IgdbId { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime? MetadataExpiresAt { get; set; }
    // Immutable backup of the previous provider's fields before the first metadata refresh.
    public string? LegacyMetadataJson { get; set; }
    public string? ProviderDetailsJson { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public DateOnly? FirstReleaseDate { get; set; }
    public List<string> Platforms { get; set; } = [];
    public List<string> Genres { get; set; } = [];
    public List<string> Developers { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int RatingCount { get; set; }
    public decimal RatingSum { get; set; }
}
