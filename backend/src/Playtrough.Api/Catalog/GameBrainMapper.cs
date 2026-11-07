using System.Globalization;
using System.Text.Json;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Catalog;

// Provider JSON stays inside the catalog boundary. Missing optional values remain unknown.
internal static class GameBrainMapper
{
    internal static JsonElement Field(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    internal static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static decimal? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
    internal static int? Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    internal static IEnumerable<JsonElement> Array(JsonElement value)
        => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static string[] Names(JsonElement value) => Array(value).Select(item => Text(Field(item, "name")))
        .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static decimal? Score(JsonElement value, decimal scale)
        => Number(value) is { } number && number is >= 0 and <= 1 ? Math.Round(number * scale, 2) : null;
    private static string? Url(JsonElement value) => Text(value) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" ? text : null;

    public static GameDetails Map(JsonElement game)
    {
        var id = Integer(Field(game, "id"));
        var name = Text(Field(game, "name"));
        if (id is null or <= 0 || string.IsNullOrWhiteSpace(name)) throw new GameCatalogUnavailableException();
        DateOnly? date = DateOnly.TryParseExact(Text(Field(game, "release_date")), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;
        var summary = Text(Field(game, "description")) ?? Text(Field(game, "short_description")) ?? string.Empty;
        if (summary.Length > 3000) summary = summary[..(char.IsHighSurrogate(summary[2999]) ? 2999 : 3000)];
        var image = Url(Field(game, "image"));
        var developer = Text(Field(game, "developer"));
        var rating = Field(game, "rating");
        return new GameDetails(id.Value, name, string.Empty, image, date, Names(Field(game, "platforms")), summary,
            Names(Field(game, "genres")), string.IsNullOrWhiteSpace(developer) ? [] : [developer], image)
        {
            SourceUrl = Url(Field(game, "link")),
            ReleaseYear = date?.Year ?? Integer(Field(game, "year")),
            ProviderRating = Score(Field(rating, "mean"), 5),
            ProviderRatingCount = Integer(Field(rating, "count")),
            CriticScore = Score(Field(rating, "mean_critics"), 100),
            Screenshots = Array(Field(game, "screenshots")).Select(Url).OfType<string>().ToArray(),
            Tags = Names(Field(game, "tags")),
            PlaytimeHours = Number(Field(Field(game, "playtime"), "median")),
            AdultOnly = Field(game, "adult_only").ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null },
            Offers = Array(Field(game, "offers")).Select(offer => {
                var price = Field(offer, "price");
                return new GameOffer(Text(Field(offer, "store_name")) ?? string.Empty, Number(Field(price, "value")),
                    Number(Field(price, "initial")), Number(Field(price, "discount_percent")), Text(Field(price, "currency")));
            }).ToArray(),
        };
    }
    public static IReadOnlyList<GameSummary> Results(JsonElement response)
    {
        var results = Field(response, "results");
        if (results.ValueKind != JsonValueKind.Array) throw new GameCatalogUnavailableException();
        return results.EnumerateArray().Select(item => (GameSummary)Map(item)).ToArray();
    }
}
