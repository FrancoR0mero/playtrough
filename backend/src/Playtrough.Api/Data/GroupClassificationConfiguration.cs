using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

internal static class GroupClassificationConfiguration
{
    public const int IdLength = 40;

    public static void Configure(ModelBuilder model)
    {
        model.Entity<GroupGenre>(entity =>
        {
            entity.Property(genre => genre.Id).HasMaxLength(IdLength);
            entity.Property(genre => genre.Name).HasMaxLength(80);
            entity.HasIndex(genre => genre.Name).IsUnique();
            entity.HasData(GroupClassificationCatalog.Genres);
        });
        model.Entity<GroupSubgenre>(entity =>
        {
            entity.Property(subgenre => subgenre.Id).HasMaxLength(IdLength);
            entity.Property(subgenre => subgenre.GenreId).HasMaxLength(IdLength);
            entity.Property(subgenre => subgenre.Name).HasMaxLength(80);
            entity.Property(subgenre => subgenre.Alias).HasMaxLength(40);
            entity.HasOne<GroupGenre>().WithMany().HasForeignKey(subgenre => subgenre.GenreId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(subgenre => subgenre.Name).IsUnique();
            entity.HasData(GroupClassificationCatalog.Subgenres);
        });
        model.Entity<GroupTag>(entity =>
        {
            entity.Property(tag => tag.Id).HasMaxLength(IdLength);
            entity.Property(tag => tag.Name).HasMaxLength(80);
            entity.Property(tag => tag.Category).HasMaxLength(20);
            entity.HasIndex(tag => tag.Name).IsUnique();
            entity.HasData(GroupClassificationCatalog.Tags);
        });
        model.Entity<GroupSubgenreAssignment>(entity =>
        {
            entity.HasKey(link => new { link.GroupId, link.SubgenreId });
            entity.Property(link => link.SubgenreId).HasMaxLength(IdLength);
            entity.HasOne(link => link.Subgenre).WithMany().HasForeignKey(link => link.SubgenreId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.SubgenreId, link.GroupId });
        });
        model.Entity<GroupTagAssignment>(entity =>
        {
            entity.HasKey(link => new { link.GroupId, link.TagId });
            entity.Property(link => link.TagId).HasMaxLength(IdLength);
            entity.HasOne(link => link.Tag).WithMany().HasForeignKey(link => link.TagId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.TagId, link.GroupId });
        });
    }
}
