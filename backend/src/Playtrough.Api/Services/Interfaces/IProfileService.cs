using Playtrough.Api.Dtos.Users;

namespace Playtrough.Api.Services.Interfaces;

public interface IProfileService
{
    Task<MyProfileDto> GetMineAsync(int userId, CancellationToken ct = default);
    Task<ProfileDto> GetAsync(int viewerId, string username, CancellationToken ct = default);
    Task<MyProfileDto> UpdateAsync(int userId, UpdateProfileRequest request, CancellationToken ct = default);
}
