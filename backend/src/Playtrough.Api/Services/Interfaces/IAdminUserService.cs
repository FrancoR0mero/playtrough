using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

public interface IAdminUserService
{
    Task<PagedResponse<AdminUserDto>> ListUsersAsync(UserListRequest request, CancellationToken ct = default);
}
