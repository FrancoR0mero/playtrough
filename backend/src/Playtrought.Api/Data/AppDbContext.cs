using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Playtrought.Api.Models;

namespace Playtrought.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity makes NormalizedUserName unique but not NormalizedEmail.
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.NormalizedUserName).IsUnique();
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        // Deterministic role seed: fixed ids and stamps keep the migration stable.
        builder.Entity<IdentityRole<int>>().HasData(
            new IdentityRole<int>
            {
                Id = 1,
                Name = "User",
                NormalizedName = "USER",
                ConcurrencyStamp = "6f1e6b2a-0000-4000-8000-000000000001",
            },
            new IdentityRole<int>
            {
                Id = 2,
                Name = "Admin",
                NormalizedName = "ADMIN",
                ConcurrencyStamp = "6f1e6b2a-0000-4000-8000-000000000002",
            });
    }
}