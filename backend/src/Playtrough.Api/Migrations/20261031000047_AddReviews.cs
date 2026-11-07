using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RatingSum",
                table: "Games",
                type: "decimal(12,1)",
                precision: 12,
                scale: 1,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<decimal>(type: "decimal(2,1)", precision: 2, scale: 1, nullable: false),
                    Text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_GameId_CreatedAt_Id",
                table: "Reviews",
                columns: new[] { "GameId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_GameId_Rating",
                table: "Reviews",
                columns: new[] { "GameId", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserId_GameId",
                table: "Reviews",
                columns: new[] { "UserId", "GameId" },
                unique: true);

            // MySQL FK cascades do not fire child-table triggers. Adjust totals on the
            // parent user BEFORE the FK removes their reviews, in the same transaction.
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
                migrationBuilder.Sql("""
                    CREATE TRIGGER TR_Users_DeleteReviewRatings BEFORE DELETE ON Users
                    BEGIN
                        UPDATE Games SET
                            RatingCount = RatingCount - (SELECT COUNT(*) FROM Reviews WHERE UserId = OLD.Id AND GameId = Games.Id),
                            RatingSum = RatingSum - (SELECT SUM(CAST(Rating AS REAL)) FROM Reviews WHERE UserId = OLD.Id AND GameId = Games.Id)
                        WHERE Id IN (SELECT GameId FROM Reviews WHERE UserId = OLD.Id);
                    END;
                    """);
            else
                migrationBuilder.Sql("""
                    CREATE TRIGGER `TR_Users_DeleteReviewRatings` BEFORE DELETE ON `Users`
                    FOR EACH ROW
                    UPDATE `Games` AS g
                    INNER JOIN (
                        SELECT `GameId`, COUNT(*) AS `ReviewCount`, SUM(`Rating`) AS `ReviewSum`
                        FROM `Reviews` WHERE `UserId` = OLD.`Id` GROUP BY `GameId`
                    ) AS removed ON removed.`GameId` = g.`Id`
                    SET g.`RatingCount` = g.`RatingCount` - removed.`ReviewCount`,
                        g.`RatingSum` = g.`RatingSum` - removed.`ReviewSum`;
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_Users_DeleteReviewRatings`;");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "RatingSum",
                table: "Games");
        }
    }
}
