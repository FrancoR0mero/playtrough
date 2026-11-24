using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class TimelineEntryCommentReactionConfiguration : IEntityTypeConfiguration<TimelineEntryCommentReaction>
{
    public void Configure(EntityTypeBuilder<TimelineEntryCommentReaction> entity)
    {
        entity.ToTable("TimelineEntryCommentReactions", table =>
            table.HasCheckConstraint("CK_TimelineEntryCommentReactions_Value", "`Value` IN (0, 1)"));
        entity.HasKey(reaction => new { reaction.CommentId, reaction.UserId });
        entity.Property(reaction => reaction.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(reaction => reaction.UpdatedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne<TimelineEntryComment>().WithMany().HasForeignKey(reaction => reaction.CommentId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(reaction => reaction.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
