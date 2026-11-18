using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class ReviewReactionConfiguration : IEntityTypeConfiguration<ReviewReaction>
{
    public void Configure(EntityTypeBuilder<ReviewReaction> entity)
    {
        entity.ToTable("ReviewReactions", table =>
            table.HasCheckConstraint("CK_ReviewReactions_Value", "`Value` IN (0, 1)"));
        entity.HasKey(reaction => new { reaction.ReviewId, reaction.UserId });
        entity.Property(reaction => reaction.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(reaction => reaction.UpdatedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne<Review>().WithMany().HasForeignKey(reaction => reaction.ReviewId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(reaction => reaction.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
