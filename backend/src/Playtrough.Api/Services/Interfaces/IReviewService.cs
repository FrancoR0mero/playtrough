using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

public interface IReviewService
{
    Task<ReviewResult> UpsertAsync(int userId, int gameId, decimal rating, string? text, CancellationToken ct = default);
    Task<RatingSummary> DeleteAsync(int userId, int gameId, CancellationToken ct = default);
    Task<ReviewResult> GetMineAsync(int userId, int gameId, CancellationToken ct = default);
    Task<RatingSummary> GetSummaryAsync(int gameId, CancellationToken ct = default);
}
