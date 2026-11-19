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
        entity.HasIndex(message => new { message.OccurredAt, message.Id });
    }
}
