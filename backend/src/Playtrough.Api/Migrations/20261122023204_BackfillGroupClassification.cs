using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Playtrough.Api.Data;

namespace Playtrough.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122023204_BackfillGroupClassification")]
public sealed class BackfillGroupClassification : Migration
{
    internal static void FillUnclassifiedGroups(MigrationBuilder migration)
    {
        foreach (var tag in new[] { "cooperativo", "competitivo", "indie", "retro" })
        {
            migration.Sql($"""
                INSERT INTO `GroupTagAssignments` (`GroupId`, `TagId`)
                SELECT g.`Id`, '{tag}'
                FROM `SocialGroups` g
                WHERE g.`GenreId` IS NULL AND LOWER(TRIM(g.`Topic`)) = '{tag}'
                  AND NOT EXISTS (
                      SELECT 1 FROM `GroupTagAssignments` a WHERE a.`GroupId` = g.`Id` AND a.`TagId` = '{tag}'
                  );
                """);
        }
        migration.Sql("UPDATE `SocialGroups` SET `GenreId` = 'otros' WHERE `GenreId` IS NULL;");
    }

    protected override void Up(MigrationBuilder migrationBuilder) => FillUnclassifiedGroups(migrationBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The legacy column is still available in this version. Keep assignments saved since the backfill.
    }
}
