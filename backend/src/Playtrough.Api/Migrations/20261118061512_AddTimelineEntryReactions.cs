using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineEntryReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommentCount",
                table: "TimelineEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DislikeCount",
                table: "TimelineEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LikeCount",
                table: "TimelineEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TimelineEntryReactions",
                columns: table => new
                {
                    EntryId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEntryReactions", x => new { x.EntryId, x.UserId });
                    table.CheckConstraint("CK_TimelineEntryReactions_Value", "`Value` IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_TimelineEntryReactions_TimelineEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "TimelineEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntryReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TimelineEntries_InteractionCounts",
                table: "TimelineEntries",
                sql: "LikeCount >= 0 AND DislikeCount >= 0 AND CommentCount >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntryReactions_EntryId_Value_CreatedAt",
                table: "TimelineEntryReactions",
                columns: new[] { "EntryId", "Value", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntryReactions_UserId",
                table: "TimelineEntryReactions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimelineEntryReactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TimelineEntries_InteractionCounts",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "DislikeCount",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "LikeCount",
                table: "TimelineEntries");
        }
    }
}
