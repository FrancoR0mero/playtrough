using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> entity)
    {
        entity.ToTable("Reviews");
        entity.HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(review => review.Rating).HasPrecision(2, 1);
        entity.Property(review => review.Text).HasMaxLength(2000).HasAnnotation("MySQL:Charset", "utf8mb4");
        entity.Property(review => review.CreatedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(review => review.EditedAt).HasConversion(LibraryConversions.Utc);
        entity.Property(review => review.ConcurrencyToken).IsConcurrencyToken();
        entity.HasIndex(review => new { review.UserId, review.GameId }).IsUnique();
        entity.HasIndex(review => new { review.GameId, review.CreatedAt, review.Id });
        entity.HasIndex(review => new { review.GameId, review.Rating });
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(review => review.UserId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Game>().WithMany().HasForeignKey(review => review.GameId).OnDelete(DeleteBehavior.Restrict);
    }
}
