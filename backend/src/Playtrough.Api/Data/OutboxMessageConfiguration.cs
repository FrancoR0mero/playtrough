using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Events;

namespace Playtrough.Api.Data;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.Property(message => message.Type).HasMaxLength(100);
        entity.Property(message => message.AggregateId).HasMaxLength(100);
        entity.Property(message => message.Payload).HasColumnType("text").HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(message => message.OccurredAt).HasConversion(LibraryConversions.Utc);
        entity.Property(message => message.NextAttemptAt).HasConversion(LibraryConversions.Utc);
        entity.Property(message => message.LeaseExpiresAt).HasConversion(LibraryConversions.Utc);
        entity.Property(message => message.ProcessedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(message => message.FailedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(message => message.Attempts).HasDefaultValue(0);
        entity.HasIndex(message => new { message.OccurredAt, message.Id });
        entity.HasIndex(message => new { message.Type, message.ProcessedAt, message.FailedAt, message.NextAttemptAt });
    }
}
