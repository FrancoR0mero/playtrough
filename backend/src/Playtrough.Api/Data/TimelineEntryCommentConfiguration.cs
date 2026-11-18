using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class TimelineEntryCommentConfiguration : IEntityTypeConfiguration<TimelineEntryComment>
{
    public void Configure(EntityTypeBuilder<TimelineEntryComment> entity)
    {
        entity.ToTable("TimelineEntryComments", table => table.HasCheckConstraint(
            "CK_TimelineEntryComments_InteractionCounts", "LikeCount >= 0 AND DislikeCount >= 0"));
        entity.Property(comment => comment.Text).HasMaxLength(TimelineEntryComment.MaxTextLength)
            .HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(comment => comment.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(comment => comment.EditedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(comment => comment.LikeCount).HasDefaultValue(0);
        entity.Property(comment => comment.DislikeCount).HasDefaultValue(0);
        entity.HasIndex(comment => new { comment.EntryId, comment.CreatedAt, comment.Id });
        entity.HasOne<TimelineEntry>().WithMany().HasForeignKey(comment => comment.EntryId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(comment => comment.AuthorId).OnDelete(DeleteBehavior.Restrict);
    }
}
