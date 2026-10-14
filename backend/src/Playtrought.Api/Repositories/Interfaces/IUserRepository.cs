using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
using Playtrought.Api.Models;

namespace Playtrought.Api.Repositories.Interfaces;

/// <summary>Persistence boundary for application users.</summary>
public interface IUserRepository
{
    Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<ApplicationUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct = default);
    Task<IdentityResult> ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);

    Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default);

    Task<IdentityResult> CreateAsync(ApplicationUser user, string password, CancellationToken ct = default);

    Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role, CancellationToken ct = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
