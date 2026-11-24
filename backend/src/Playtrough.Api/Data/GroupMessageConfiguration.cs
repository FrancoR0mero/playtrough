using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class GroupMessageConfiguration : IEntityTypeConfiguration<GroupMessage>
{
    public void Configure(EntityTypeBuilder<GroupMessage> entity)
    {
        entity.Property(message => message.Text).HasMaxLength(GroupMessage.MaxTextLength)
            .HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(message => message.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(message => message.RemovedAt).HasConversion(LibraryConversions.Utc);
        entity.HasIndex(message => new { message.AuthorId, message.ClientMessageId }).IsUnique();
        entity.HasIndex(message => new { message.GroupId, message.CreatedAt, message.Id });
        entity.HasIndex(message => new { message.GroupId, message.Id });
        entity.HasOne<SocialGroup>().WithMany().HasForeignKey(message => message.GroupId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(message => message.AuthorId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(message => message.RemovedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GroupMessageReceiptConfiguration : IEntityTypeConfiguration<GroupMessageReceipt>
{
    public void Configure(EntityTypeBuilder<GroupMessageReceipt> entity)
    {
        entity.HasKey(receipt => new { receipt.MessageId, receipt.RecipientId });
        entity.Property(receipt => receipt.ReceivedAt).HasConversion(LibraryConversions.Utc);
        entity.HasOne<GroupMessage>().WithMany().HasForeignKey(receipt => receipt.MessageId);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(receipt => receipt.RecipientId);
    }
}
