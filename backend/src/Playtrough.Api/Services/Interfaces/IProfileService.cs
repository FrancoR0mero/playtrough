using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Dtos.Friends;

namespace Playtrough.Api.Services.Interfaces;

public interface IProfileService
{
    Task<MyProfileDto> GetMineAsync(int userId, CancellationToken ct = default);
    Task<ProfileDto> GetAsync(int viewerId, string username, CancellationToken ct = default);
    Task<FriendsPage<FriendDto>> FriendsAsync(int viewerId, string username, FriendsPageRequest request, CancellationToken ct = default);
    Task<MyProfileDto> UpdateAsync(int userId, UpdateProfileRequest request, CancellationToken ct = default);
}
