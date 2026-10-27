using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> entity)
    {
        entity.ToTable("Games");
        entity.Property(game => game.Provider).HasMaxLength(32).IsRequired();
        entity.Property(game => game.Name).HasMaxLength(300).IsRequired();
        entity.Property(game => game.Slug).HasMaxLength(300).IsRequired();
        entity.Property(game => game.Summary).HasColumnType("text").IsRequired();
        entity.Property(game => game.CoverImageUrl).HasMaxLength(2048);
        // Connector/NET reads MySQL DATE as DateTime; convert explicitly to the domain DateOnly.
        entity.Property(game => game.FirstReleaseDate)
            .HasConversion(new ValueConverter<DateOnly, DateTime>(
                date => date.ToDateTime(TimeOnly.MinValue),
                dateTime => DateOnly.FromDateTime(dateTime)))
            .HasColumnType("date");
        entity.HasIndex(game => new { game.Provider, game.ExternalId }).IsUnique();

        // Plain JSON strings work with the existing Oracle MySQL provider and SQLite
        // tests without provider-specific owned-JSON features or auxiliary tables.
        // A deep comparer also detects in-place list edits, not just replacements.
        var converter = new ValueConverter<List<string>, string>(
            values => JsonSerializer.Serialize(values, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());
        var comparer = new ValueComparer<List<string>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
            values => values.ToList());
        foreach (var name in new[] { nameof(Game.Platforms), nameof(Game.Genres), nameof(Game.Developers) })
            entity.Property<List<string>>(name).HasColumnType("json").HasConversion(converter, comparer).IsRequired();

        // Both providers discard DateTime.Kind on storage. All catalog timestamps are UTC.
        entity.Property(game => game.CreatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        entity.Property(game => game.UpdatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
