using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> entity)
    {
        entity.ToTable("UserBlocks", table => table.HasCheckConstraint("CK_UserBlocks_DifferentUsers", "`BlockerId` <> `BlockedId`"));
        entity.HasKey(b => new { b.BlockerId, b.BlockedId });
        entity.HasIndex(b => new { b.BlockedId, b.BlockerId });
        entity.Property(b => b.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Restrict);
    }
}
