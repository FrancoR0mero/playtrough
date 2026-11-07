using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Data;

internal sealed class LibraryEntryConfiguration : IEntityTypeConfiguration<LibraryEntry>
{
    public void Configure(EntityTypeBuilder<LibraryEntry> entity)
    {
        entity.ToTable("LibraryEntries");
        entity.Property(entry => entry.Status).HasConversion(LibraryConversions.Status).HasMaxLength(16);
        entity.Property(entry => entry.StatusChangedOn).HasConversion(LibraryConversions.Date).HasColumnType("date");
        entity.Property(entry => entry.AddedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(entry => entry.ConcurrencyToken).IsConcurrencyToken();
        entity.HasIndex(entry => new { entry.UserId, entry.GameId }).IsUnique();
        entity.HasIndex(entry => new { entry.UserId, entry.Status });
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(entry => entry.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(entry => entry.GameId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LibraryEventConfiguration : IEntityTypeConfiguration<LibraryEvent>
{
    public void Configure(EntityTypeBuilder<LibraryEvent> entity)
    {
        entity.ToTable("LibraryEvents");
        entity.Property(change => change.Type).HasConversion(
            type => LibraryValues.EventValue(type), value => LibraryValues.ParseEvent(value)).HasMaxLength(16);
        entity.Property(change => change.FromStatus).HasConversion(LibraryConversions.Status).HasMaxLength(16);
        entity.Property(change => change.ToStatus).HasConversion(LibraryConversions.Status).HasMaxLength(16);
        entity.Property(change => change.OccurredOn).HasConversion(LibraryConversions.Date).HasColumnType("date");
        entity.Property(change => change.RecordedAt).HasConversion(LibraryConversions.Utc);
        entity.HasIndex(change => new { change.UserId, change.OccurredOn, change.Id });
        entity.HasIndex(change => new { change.UserId, change.GameId });
        // Events belong to the user and game, never to the removable library entry.
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(change => change.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(change => change.GameId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class LibraryConversions
{
    // Explicit text mapping preserves the public contract if enum names/ordinals change.
    public static readonly ValueConverter<LibraryStatus, string> Status = new(
        status => LibraryValues.StatusValue(status), value => LibraryValues.ParseStatus(value));
    // Match GameConfiguration: Oracle Connector/NET materializes MySQL DATE as DateTime.
    public static readonly ValueConverter<DateOnly, DateTime> Date = new(
        date => date.ToDateTime(TimeOnly.MinValue), value => DateOnly.FromDateTime(value));
    public static readonly ValueConverter<DateTime, DateTime> Utc = new(
        value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
