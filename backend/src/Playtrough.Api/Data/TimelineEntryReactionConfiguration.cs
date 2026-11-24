using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class TimelineEntryReactionConfiguration : IEntityTypeConfiguration<TimelineEntryReaction>
{
    public void Configure(EntityTypeBuilder<TimelineEntryReaction> entity)
    {
        entity.ToTable("TimelineEntryReactions", table =>
            table.HasCheckConstraint("CK_TimelineEntryReactions_Value", "`Value` IN (0, 1)"));
        entity.HasKey(reaction => new { reaction.EntryId, reaction.UserId });
        entity.HasIndex(reaction => new { reaction.EntryId, reaction.Value, reaction.CreatedAt });
        entity.Property(reaction => reaction.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(reaction => reaction.UpdatedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne<TimelineEntry>().WithMany().HasForeignKey(reaction => reaction.EntryId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(reaction => reaction.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
