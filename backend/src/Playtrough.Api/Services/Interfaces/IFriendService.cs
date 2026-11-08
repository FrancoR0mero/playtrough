using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Users;

namespace Playtrough.Api.Services.Interfaces;

public interface IFriendService
{
    Task<SentFriendRequestDto> SendRequestAsync(int senderId, string username, CancellationToken ct = default);
    Task<AcceptedFriendDto> AcceptAsync(int userId, int requestId, CancellationToken ct = default);
    Task RejectAsync(int userId, int requestId, CancellationToken ct = default);
    Task CancelAsync(int userId, int requestId, CancellationToken ct = default);
    Task RemoveFriendAsync(int userId, string username, CancellationToken ct = default);
    Task<FriendsPage<FriendDto>> ListAsync(int userId, string? q, FriendsPageRequest page, CancellationToken ct = default);
    Task<FriendsPage<FriendRequestDto>> RequestsAsync(int userId, bool received, FriendsPageRequest page, CancellationToken ct = default);
    Task<FriendsSummaryDto> SummaryAsync(int userId, CancellationToken ct = default);
    Task<RelationshipDto> RelationshipAsync(int viewerId, int otherId, CancellationToken ct = default);
}
