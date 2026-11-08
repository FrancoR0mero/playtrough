using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FriendRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SenderId = table.Column<int>(type: "int", nullable: false),
                    ReceiverId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PendingPairKey = table.Column<long>(type: "bigint", nullable: true, computedColumnSql: "CASE WHEN `Status` = 0 THEN CASE WHEN `SenderId` < `ReceiverId` THEN `SenderId` * 4294967296 + `ReceiverId` ELSE `ReceiverId` * 4294967296 + `SenderId` END ELSE NULL END", stored: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendRequests", x => x.Id);
                    table.CheckConstraint("CK_FriendRequests_DifferentUsers", "`SenderId` <> `ReceiverId`");
                    table.ForeignKey(
                        name: "FK_FriendRequests_Users_ReceiverId",
                        column: x => x.ReceiverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FriendRequests_Users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    UserLowId = table.Column<int>(type: "int", nullable: false),
                    UserHighId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => new { x.UserLowId, x.UserHighId });
                    table.CheckConstraint("CK_Friendships_CanonicalPair", "`UserLowId` < `UserHighId`");
                    table.ForeignKey(
                        name: "FK_Friendships_Users_UserHighId",
                        column: x => x.UserHighId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Friendships_Users_UserLowId",
                        column: x => x.UserLowId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_PendingPairKey",
                table: "FriendRequests",
                column: "PendingPairKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_ReceiverId_Status_CreatedAt_Id",
                table: "FriendRequests",
                columns: new[] { "ReceiverId", "Status", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_SenderId_Status_CreatedAt_Id",
                table: "FriendRequests",
                columns: new[] { "SenderId", "Status", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_UserHighId",
                table: "Friendships",
                column: "UserHighId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendRequests");

            migrationBuilder.DropTable(
                name: "Friendships");
        }
    }
}
