using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TimelineEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "date", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LibraryEventId = table.Column<int>(type: "int", nullable: true),
                    ReviewId = table.Column<int>(type: "int", nullable: true),
                    IsHidden = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    FeedEligible = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_LibraryEvents_LibraryEventId",
                        column: x => x.LibraryEventId,
                        principalTable: "LibraryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimelineEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_FeedEligible_UserId_RecordedAt_Id",
                table: "TimelineEntries",
                columns: new[] { "FeedEligible", "UserId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_GameId",
                table: "TimelineEntries",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_LibraryEventId",
                table: "TimelineEntries",
                column: "LibraryEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_ReviewId",
                table: "TimelineEntries",
                column: "ReviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_UserId_OccurredOn_RecordedAt_Id",
                table: "TimelineEntries",
                columns: new[] { "UserId", "OccurredOn", "RecordedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimelineEntries");
        }
    }
}
