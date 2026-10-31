using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Dtos.Reviews;

public sealed record MyReviewDto(int Id, int GameId, decimal Rating, string? Text, DateTime CreatedAt, DateTime? EditedAt)
{
    public static MyReviewDto FromReview(Review review)
        => new(review.Id, review.GameId, review.Rating, review.Text, review.CreatedAt, review.EditedAt);
}

public sealed record UpsertReviewResponse(MyReviewDto Review, RatingSummary RatingSummary);
public sealed record DeleteReviewResponse(RatingSummary RatingSummary);

public sealed record ReviewAuthorDto(int UserId, string Username);
public sealed record ReviewListItemDto(int Id, decimal Rating, string? Text, DateTime CreatedAt, DateTime? EditedAt,
    ReviewAuthorDto Author, bool IsMine);
public sealed record ReviewListResponse(IReadOnlyList<ReviewListItemDto> Items, RatingSummary RatingSummary,
    int Page, int PageSize, bool HasMore);
