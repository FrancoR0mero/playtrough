using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Playtrough.Api.Authorization;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;

namespace Playtrough.Api.Repositories.Implementations;

public sealed class AdminUserRepository(AppDbContext db, UserManager<ApplicationUser> users) : IAdminUserRepository
{
    public async Task<PagedResponse<AdminUserDto>> ListUsersAsync(UserListRequest request, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var name = users.NormalizeName(request.Search.Trim())!;
            var email = users.NormalizeEmail(request.Search.Trim())!;
            query = query.Where(user => user.NormalizedUserName != null && user.NormalizedUserName.Contains(name) ||
                user.NormalizedEmail != null && user.NormalizedEmail.Contains(email));
        }
        if (request.Role is not null)
            query = query.Where(user => (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
                where link.UserId == user.Id && role.Name == request.Role select link).Any());

        var total = await query.CountAsync(ct);
        var offset = ((long)request.Page - 1) * request.PageSize;
        var items = offset > int.MaxValue ? [] : await query.OrderBy(user => user.Id).Skip((int)offset).Take(request.PageSize)
            .Select(user => new AdminUserDto(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty,
                (from link in db.UserRoles join role in db.Roles on link.RoleId equals role.Id
                 where link.UserId == user.Id select role.Name).FirstOrDefault() ?? string.Empty, user.EmailConfirmed))
            .ToListAsync(ct);
        return new(items, total, request.Page, request.PageSize);
    }

    public async Task<IDbContextTransaction> BeginRoleChangeAsync(CancellationToken ct = default)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var adminName = users.NormalizeName(AppRoles.Admin);
            // First SQL statement: lock the shared Admin role row with a no-op UPDATE.
            // InnoDB serializes role changes across API instances; SQLite obtains its writer lock.
            // No snapshot reads precede this lock, so the admin count sees the previous committed change.
            // Keeping the value unchanged also leaves idempotent requests entirely unchanged.
            await db.Roles.Where(role => role.NormalizedName == adminName)
                .ExecuteUpdateAsync(update => update.SetProperty(role => role.ConcurrencyStamp, role => role.ConcurrencyStamp), ct);
            if (!await db.Roles.AnyAsync(role => role.NormalizedName == adminName, ct))
                throw new InvalidOperationException("The required admin role is unavailable.");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

}
