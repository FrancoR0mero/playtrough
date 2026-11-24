using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetireGroupTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reconcile groups written by a previous API instance after the initial backfill.
            BackfillGroupClassification.FillUnclassifiedGroups(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "LegacyTopic",
                table: "SocialGroups",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE `SocialGroups`
                SET `LegacyTopic` = `Topic`
                WHERE LOWER(TRIM(`Topic`)) NOT IN ('cooperativo', 'competitivo', 'indie', 'retro', 'general');
                """);

            migrationBuilder.DropIndex(
                name: "IX_SocialGroups_DeletedAt_Topic_Id",
                table: "SocialGroups");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "SocialGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialGroups_GroupGenres_GenreId",
                table: "SocialGroups");

            migrationBuilder.AlterColumn<string>(
                name: "GenreId",
                table: "SocialGroups",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40,
                oldNullable: true);

            RestoreGenreForeignKey(migrationBuilder);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "SocialGroups",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE `SocialGroups` g SET g.`Topic` = COALESCE(g.`LegacyTopic`, (
                    SELECT CASE a.`TagId`
                        WHEN 'cooperativo' THEN 'Cooperativo'
                        WHEN 'competitivo' THEN 'Competitivo'
                        WHEN 'retro' THEN 'Retro'
                        WHEN 'indie' THEN 'Indie'
                    END
                    FROM `GroupTagAssignments` a
                    WHERE a.`GroupId` = g.`Id` AND a.`TagId` IN ('cooperativo', 'competitivo', 'retro', 'indie')
                    ORDER BY a.`TagId`
                    LIMIT 1
                ), 'General');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Topic",
                table: "SocialGroups",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50,
                oldNullable: true)
                .Annotation("MySQL:Charset", "utf8mb4")
                .OldAnnotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.DropColumn(
                name: "LegacyTopic",
                table: "SocialGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialGroups_GroupGenres_GenreId",
                table: "SocialGroups");

            migrationBuilder.AlterColumn<string>(
                name: "GenreId",
                table: "SocialGroups",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40);

            RestoreGenreForeignKey(migrationBuilder);

            migrationBuilder.CreateIndex(
                name: "IX_SocialGroups_DeletedAt_Topic_Id",
                table: "SocialGroups",
                columns: new[] { "DeletedAt", "Topic", "Id" });
        }

        private static void RestoreGenreForeignKey(MigrationBuilder migrationBuilder)
            => migrationBuilder.AddForeignKey(
                name: "FK_SocialGroups_GroupGenres_GenreId",
                table: "SocialGroups",
                column: "GenreId",
                principalTable: "GroupGenres",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
    }
}
