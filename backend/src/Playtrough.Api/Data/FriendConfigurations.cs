using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> entity)
    {
        entity.ToTable("FriendRequests", table => table.HasCheckConstraint("CK_FriendRequests_DifferentUsers", "`SenderId` <> `ReceiverId`"));
        entity.Property(r => r.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(r => r.RespondedAt).HasConversion(LibraryConversions.Utc);
        // Positive int IDs fit losslessly in a signed BIGINT canonical pair. NULLs
        // allow unlimited history, while the unique index protects both directions.
        entity.Property(r => r.PendingPairKey).HasComputedColumnSql(
            "CASE WHEN `Status` = 0 THEN CASE WHEN `SenderId` < `ReceiverId` " +
            "THEN `SenderId` * 4294967296 + `ReceiverId` ELSE `ReceiverId` * 4294967296 + `SenderId` END ELSE NULL END", stored: false);
        entity.HasIndex(r => r.PendingPairKey).IsUnique();
        entity.HasIndex(r => new { r.ReceiverId, r.Status, r.CreatedAt, r.Id });
        entity.HasIndex(r => new { r.SenderId, r.Status, r.CreatedAt, r.Id });
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.SenderId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ReceiverId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> entity)
    {
        entity.ToTable("Friendships", table => table.HasCheckConstraint("CK_Friendships_CanonicalPair", "`UserLowId` < `UserHighId`"));
        entity.HasKey(f => new { f.UserLowId, f.UserHighId });
        // The primary key already indexes UserLowId.
        entity.HasIndex(f => f.UserHighId);
        entity.Property(f => f.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.UserLowId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(f => f.UserHighId).OnDelete(DeleteBehavior.Restrict);
    }
}
