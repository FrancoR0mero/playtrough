using Microsoft.EntityFrameworkCore.Storage;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Models;

namespace Playtrough.Api.Repositories.Interfaces;

public interface IAdminUserRepository
{
    Task<PagedResponse<AdminUserDto>> ListUsersAsync(UserListRequest request, CancellationToken ct = default);
    Task<IDbContextTransaction> BeginRoleChangeAsync(CancellationToken ct = default);
}
