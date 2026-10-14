using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Playtrought.Api.Data;
using Playtrought.Api.Models;
using Playtrought.Api.Repositories.Interfaces;

namespace Playtrought.Api.Repositories.Implementations;

/// <summary>User persistence backed by ASP.NET Core Identity and the app DbContext.
/// Contains no business logic; duplicate checks compare normalized values.</summary>
public class UserRepository(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext) : IUserRepository
{
    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = userManager.NormalizeEmail(email);
        return dbContext.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
    }

    public Task<ApplicationUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = userManager.NormalizeName(username);
        return dbContext.Users.SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
    }

    public Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken ct = default)
        => userManager.GetRolesAsync(user);

    public Task<IdentityResult> ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken ct = default)
        => userManager.ResetAccessFailedCountAsync(user);

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = userManager.NormalizeEmail(email);
        return dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct);
    }

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = userManager.NormalizeName(username);
        return dbContext.Users.AnyAsync(u => u.NormalizedUserName == normalized, ct);
    }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, string password, CancellationToken ct = default)
        => userManager.CreateAsync(user, password);

    public Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role, CancellationToken ct = default)
        => userManager.AddToRoleAsync(user, role);

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => await dbContext.Database.BeginTransactionAsync(ct);
}
