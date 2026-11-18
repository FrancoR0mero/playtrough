using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Data;

internal sealed class TimelineEntryConfiguration : IEntityTypeConfiguration<TimelineEntry>
{
    public void Configure(EntityTypeBuilder<TimelineEntry> entity)
    {
        entity.ToTable("TimelineEntries", table => table.HasCheckConstraint("CK_TimelineEntries_InteractionCounts",
            "LikeCount >= 0 AND DislikeCount >= 0 AND CommentCount >= 0"));
        entity.Property(entry => entry.LikeCount).HasDefaultValue(0);
        entity.Property(entry => entry.DislikeCount).HasDefaultValue(0);
        entity.Property(entry => entry.CommentCount).HasDefaultValue(0);
        entity.Property(entry => entry.Type).HasConversion(
            value => TimelineValues.TypeValue(value), value => TimelineValues.ParseType(value))
            .HasMaxLength(16);
        entity.Property(entry => entry.FromStatus).HasConversion(LibraryConversions.Status).HasMaxLength(16);
        entity.Property(entry => entry.ToStatus).HasConversion(LibraryConversions.Status).HasMaxLength(16);
        entity.Property(entry => entry.OccurredOn).HasConversion(LibraryConversions.Date).HasColumnType("date");
        entity.Property(entry => entry.RecordedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(entry => entry.IsHidden).HasDefaultValue(false);
        entity.HasIndex(entry => entry.LibraryEventId).IsUnique();
        entity.HasIndex(entry => entry.ReviewId).IsUnique();
        entity.HasIndex(entry => new { entry.UserId, entry.OccurredOn, entry.RecordedAt, entry.Id });
        entity.HasIndex(entry => new { entry.FeedEligible, entry.UserId, entry.RecordedAt, entry.Id });
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(entry => entry.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(entry => entry.GameId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(entry => entry.LibraryEvent).WithMany().HasForeignKey(entry => entry.LibraryEventId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(entry => entry.Review).WithMany().HasForeignKey(entry => entry.ReviewId).OnDelete(DeleteBehavior.Cascade);
    }
}
