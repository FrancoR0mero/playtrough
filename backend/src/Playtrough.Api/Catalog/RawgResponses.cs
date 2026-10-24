using System.Text.Json.Serialization;

namespace Playtrough.Api.Catalog;

internal sealed class RawgSearchResponse
{
    public string? Next { get; init; }
    public required List<RawgGameResponse?> Results { get; init; }
}

internal sealed class RawgGameResponse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? Released { get; init; }
    [JsonPropertyName("background_image")] public string? BackgroundImage { get; init; }
    [JsonPropertyName("description_raw")] public string? DescriptionRaw { get; init; }
    // Deliberately no `description` property: upstream HTML is never used.
    public List<RawgPlatform?>? Platforms { get; init; }
    public List<RawgName?>? Genres { get; init; }
    public List<RawgName?>? Developers { get; init; }
}

internal sealed class RawgPlatform
{
    public RawgName? Platform { get; init; }
}

internal sealed class RawgName
{
    public string? Name { get; init; }
}
