using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Models;
using Playtrough.Api.Authorization;

namespace Playtrough.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RoleChangeLog> RoleChangeLogs => Set<RoleChangeLog>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<LibraryEntry> LibraryEntries => Set<LibraryEntry>();
    public DbSet<LibraryEvent> LibraryEvents => Set<LibraryEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new GameConfiguration());
        builder.ApplyConfiguration(new ReviewConfiguration());
        builder.ApplyConfiguration(new FriendRequestConfiguration());
        builder.ApplyConfiguration(new FriendshipConfiguration());
        builder.ApplyConfiguration(new UserBlockConfiguration());
        builder.ApplyConfiguration(new LibraryEntryConfiguration());
        builder.ApplyConfiguration(new LibraryEventConfiguration());

        // Identity tables use domain names, not the framework's AspNet* prefix.
        // Data is preserved by RENAME TABLE (never recreated) in migrations.
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<IdentityRole<int>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<int>>().ToTable("UserRoles");
        builder.Entity<IdentityRoleClaim<int>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserClaim<int>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<int>>().ToTable("UserTokens");

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.ReplacedByToken).HasMaxLength(64);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId);
        });

        builder.Entity<RoleChangeLog>(entity =>
        {
            entity.ToTable("RoleChangeLogs");
            entity.Property(change => change.OldRole).HasMaxLength(20).IsRequired();
            entity.Property(change => change.NewRole).HasMaxLength(20).IsRequired();
            // MySQL datetime and SQLite do not preserve DateTime.Kind; values are stored in UTC.
            entity.Property(change => change.ChangedAt).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(change => change.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(change => change.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(change => new { change.ChangedAt, change.Id });
            entity.HasIndex(change => new { change.TargetUserId, change.ChangedAt });
        });

        // Identity makes NormalizedUserName unique but not NormalizedEmail.
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.AvatarFileName).HasMaxLength(37);
            entity.Property(u => u.Bio).HasMaxLength(300).HasAnnotation("MySQL:Charset", "utf8mb4");
            // Always write explicit values, including Public=0 for history, rather
            // than treating enum zero as a sentinel for the database's Friends default.
            entity.Property(u => u.ProfileVisibility).HasDefaultValue(Visibility.Public).ValueGeneratedNever();
            entity.Property(u => u.HistoryVisibility).HasDefaultValue(Visibility.Friends).ValueGeneratedNever();
            entity.Property(u => u.UsernameChangedAt).HasConversion(
                value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);
            entity.Property(u => u.CreatedAt).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasIndex(u => u.NormalizedUserName).IsUnique();
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
        });

        // Deterministic role seed: fixed ids and stamps keep the migration stable.
        builder.Entity<IdentityRole<int>>().HasData(
            new IdentityRole<int>
            {
                Id = 1,
                Name = AppRoles.User,
                NormalizedName = "USER",
                ConcurrencyStamp = "6f1e6b2a-0000-4000-8000-000000000001",
            },
            new IdentityRole<int>
            {
                Id = 2,
                Name = AppRoles.Admin,
                NormalizedName = "ADMIN",
                ConcurrencyStamp = "6f1e6b2a-0000-4000-8000-000000000002",
            });
    }
}
