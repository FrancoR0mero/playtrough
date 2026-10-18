using Microsoft.AspNetCore.Identity;
using Playtrough.Api.Authorization;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class AdminUserService(IAdminUserRepository repository, IUserRepository userRepository,
    UserManager<ApplicationUser> users, ILogger<AdminUserService> logger) : IAdminUserService
{
    public Task<PagedResponse<AdminUserDto>> ListUsersAsync(UserListRequest request, CancellationToken ct = default)
        => repository.ListUsersAsync(request, ct);

    public async Task<RoleChangeResult> ChangeRoleAsync(int actorId, int targetId, string role, CancellationToken ct = default)
    {
        if (!AppRoles.IsValid(role)) return new(null, RoleChangeError.InvalidRole);
        try
        {
            await using var transaction = await repository.BeginRoleChangeAsync(ct);
            var user = await userRepository.FindByIdAsync(targetId, ct);
            if (user is null) return new(null, RoleChangeError.NotFound);
            var currentRoles = await users.GetRolesAsync(user);
            if (currentRoles.Count != 1 || !AppRoles.IsValid(currentRoles[0]))
                return new(null, RoleChangeError.InvalidRoleState);
            var oldRole = currentRoles[0];
            if (oldRole == role) return new(ToDto(user, role));
            if (actorId == targetId) return new(null, RoleChangeError.SelfRoleChange);
            if (oldRole == AppRoles.Admin && await repository.CountAdminsAsync(ct) <= 1)
                return new(null, RoleChangeError.LastAdmin);

            if (!(await users.RemoveFromRoleAsync(user, oldRole)).Succeeded ||
                !(await users.AddToRoleAsync(user, role)).Succeeded)
                return new(null, RoleChangeError.Unexpected);

            await transaction.CommitAsync(ct);
            return new(ToDto(user, role));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Role change failed ({ExceptionType}).", ex.GetType().Name);
            return new(null, RoleChangeError.Unexpected);
        }
    }

    private static AdminUserDto ToDto(ApplicationUser user, string role) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, role, user.EmailConfirmed);
}
