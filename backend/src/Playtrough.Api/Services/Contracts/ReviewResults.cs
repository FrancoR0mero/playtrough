using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Contracts;

public sealed record RatingSummary(decimal? Average, int Count)
{
    public static RatingSummary FromTotals(int count, decimal sum)
        => new(count == 0 ? null : Math.Round(sum / count, 1, MidpointRounding.AwayFromZero), count);
}

public sealed record ReviewResult(Review Review, RatingSummary RatingSummary, bool Created = false);
