using System.Text.Json;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Dtos.Reviews;

/// <summary>Keep the JSON types intact so invalid input uses the review error contract.</summary>
public sealed class UpsertReviewRequest
{
    public JsonElement Rating { get; init; }
    public JsonElement Text { get; init; }

    public decimal ReadRating()
        => Rating.ValueKind == JsonValueKind.Number && Rating.TryGetDecimal(out var rating)
            // Reject numbers that only look like a valid half-star after decimal rounding.
            && JsonElement.DeepEquals(Rating, JsonSerializer.SerializeToElement(rating))
            ? rating : throw new ReviewException("invalid_rating");

    public string? ReadText() => Text.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.String => Text.GetString(),
        _ => throw new ReviewException("invalid_text"),
    };
}
