using Playtrough.Api.Dtos.Users;

namespace Playtrough.Api.Services.Interfaces;

public interface IUserSearchService
{
    Task<UserSearchResponse> SearchAsync(int viewerId, UserSearchRequest request, CancellationToken ct = default);
}
