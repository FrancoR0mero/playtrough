using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.Property(notification => notification.Type).HasMaxLength(64);
        entity.Property(notification => notification.ResourceType).HasMaxLength(32);
        entity.Property(notification => notification.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(notification => notification.ReadAt).HasConversion(LibraryConversions.Utc);
        entity.HasIndex(notification => new { notification.RecipientId, notification.EventId, notification.Type }).IsUnique();
        entity.HasIndex(notification => new { notification.RecipientId, notification.CreatedAt, notification.Id });
        entity.HasIndex(notification => new { notification.RecipientId, notification.ReadAt, notification.CreatedAt, notification.Id });
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(notification => notification.RecipientId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(notification => notification.ActorId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> entity)
    {
        entity.HasKey(preference => new { preference.UserId, preference.Category });
        entity.Property(preference => preference.Category).HasMaxLength(32);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(preference => preference.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
