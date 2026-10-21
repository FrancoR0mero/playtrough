using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Playtrough.Api.Authorization;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

public sealed class IdentitySeeder(
    AppDbContext db,
    RoleManager<IdentityRole<int>> roles,
    UserManager<ApplicationUser> users,
    IOptions<AdminSeedSettings> options,
    ILogger<IdentitySeeder> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Reuse the existing User/Admin seed. Create only missing roles, including Moderator.
        // Schema migrations remain an explicit deployment step, never applied at startup.
        foreach (var role in AppRoles.All)
        {
            ct.ThrowIfCancellationRequested();
            if (await roles.RoleExistsAsync(role)) continue;
            var result = await roles.CreateAsync(new IdentityRole<int>(role));
            if (!result.Succeeded && !await roles.RoleExistsAsync(role))
                throw new InvalidOperationException("Could not initialize a required application role.");
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Email) ||
            string.IsNullOrWhiteSpace(settings.Password))
        {
            logger.LogWarning("Initial admin configuration is absent or incomplete; admin creation skipped.");
            return;
        }

        var username = settings.Username.Trim();
        var email = settings.Email.Trim();
        // Never promote, reset or otherwise change an existing account, even if only one field matches.
        if (await users.FindByNameAsync(username) is not null || await users.FindByEmailAsync(email) is not null)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var admin = new ApplicationUser { UserName = username, Email = email, EmailConfirmed = true };
        if (!(await users.CreateAsync(admin, settings.Password)).Succeeded ||
            !(await users.AddToRoleAsync(admin, AppRoles.Admin)).Succeeded)
            // Identity errors can echo account data. Do not log options, passwords or error descriptions.
            throw new InvalidOperationException("Could not initialize the configured admin account.");
        await transaction.CommitAsync(ct);
    }
}
