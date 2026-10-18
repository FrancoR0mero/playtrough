using Microsoft.AspNetCore.Identity;
using Playtrough.Api.Authorization;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class AdminUserService(IAdminUserRepository repository) : IAdminUserService
{
    public Task<PagedResponse<AdminUserDto>> ListUsersAsync(UserListRequest request, CancellationToken ct = default)
        => repository.ListUsersAsync(request, ct);

}
