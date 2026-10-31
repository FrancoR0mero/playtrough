using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Contracts;

public sealed record RatingSummary(decimal? Average, int Count)
{
    public static RatingSummary FromTotals(int count, decimal sum)
        // Preserve one decimal place when System.Text.Json serializes whole averages (4.0).
        => new(count == 0 ? null : Math.Round(sum / count, 1, MidpointRounding.AwayFromZero) + 0.0m, count);
}

public sealed record ReviewResult(Review Review, RatingSummary RatingSummary, bool Created = false);
