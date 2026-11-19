using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class SocialGroupConfiguration : IEntityTypeConfiguration<SocialGroup>
{
    public void Configure(EntityTypeBuilder<SocialGroup> entity)
    {
        entity.Property(group => group.Name).HasMaxLength(SocialGroup.MaxNameLength)
            .HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(group => group.Description).HasMaxLength(SocialGroup.MaxDescriptionLength)
            .HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(group => group.Topic).HasMaxLength(SocialGroup.MaxTopicLength)
            .HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(group => group.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(group => group.DeletedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(group => group.ConcurrencyToken).IsConcurrencyToken();
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(group => group.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(group => new { group.DeletedAt, group.Id });
        entity.HasIndex(group => new { group.DeletedAt, group.Topic, group.Id });
    }
}

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> entity)
    {
        entity.HasKey(member => new { member.GroupId, member.UserId });
        entity.Property(member => member.JoinedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(member => member.RemovedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne(member => member.Group).WithMany().HasForeignKey(member => member.GroupId);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(member => member.UserId);
    }
}

internal sealed class GroupJoinRequestConfiguration : IEntityTypeConfiguration<GroupJoinRequest>
{
    public void Configure(EntityTypeBuilder<GroupJoinRequest> entity)
    {
        entity.Property(request => request.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(request => request.ResolvedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(request => request.PendingUserId).HasComputedColumnSql(
            "CASE WHEN `Status` = 0 THEN `UserId` ELSE NULL END", stored: false);
        entity.HasIndex(request => new { request.GroupId, request.PendingUserId }).IsUnique();
        entity.HasIndex(request => new { request.GroupId, request.Status, request.Id });
        entity.HasIndex(request => new { request.GroupId, request.UserId, request.Id });
        entity.HasOne<SocialGroup>().WithMany().HasForeignKey(request => request.GroupId);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(request => request.UserId);
    }
}
