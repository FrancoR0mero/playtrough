using System.Text.Json;
using Playtrough.Api.Dtos.Users;

namespace Playtrough.Api.Dtos.Friends;

public sealed class SendFriendRequest { public JsonElement Username { get; init; } }
public sealed record SentFriendRequestDto(int Id, string Status, UserSummaryDto User, DateTime CreatedAt);
public sealed record FriendRequestDto(int Id, UserSummaryDto User, DateTime CreatedAt);
public sealed record FriendDto(UserSummaryDto User, DateTime Since);
public sealed record AcceptedFriendDto(UserSummaryDto Friend, DateTime Since);
public sealed record FriendsSummaryDto(int FriendsCount, int PendingReceivedCount, int PendingSentCount);
public sealed record FriendsPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);
public sealed class FriendsPageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
