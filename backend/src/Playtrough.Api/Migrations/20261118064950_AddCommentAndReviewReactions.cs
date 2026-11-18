using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentAndReviewReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DislikeCount",
                table: "Reviews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LikeCount",
                table: "Reviews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReviewReactions",
                columns: table => new
                {
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewReactions", x => new { x.ReviewId, x.UserId });
                    table.CheckConstraint("CK_ReviewReactions_Value", "`Value` IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_ReviewReactions_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TimelineEntryCommentReactions",
                columns: table => new
                {
                    CommentId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEntryCommentReactions", x => new { x.CommentId, x.UserId });
                    table.CheckConstraint("CK_TimelineEntryCommentReactions_Value", "`Value` IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_TimelineEntryCommentReactions_TimelineEntryComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "TimelineEntryComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntryCommentReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reviews_InteractionCounts",
                table: "Reviews",
                sql: "LikeCount >= 0 AND DislikeCount >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReactions_UserId",
                table: "ReviewReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntryCommentReactions_UserId",
                table: "TimelineEntryCommentReactions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewReactions");

            migrationBuilder.DropTable(
                name: "TimelineEntryCommentReactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reviews_InteractionCounts",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "DislikeCount",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "LikeCount",
                table: "Reviews");
        }
    }
}
