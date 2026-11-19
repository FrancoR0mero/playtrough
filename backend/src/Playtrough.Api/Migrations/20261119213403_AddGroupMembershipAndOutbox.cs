using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMembershipAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "GroupMembers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupJoinRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PendingUserId = table.Column<int>(type: "int", nullable: true, computedColumnSql: "CASE WHEN `Status` = 0 THEN `UserId` ELSE NULL END", stored: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupJoinRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupJoinRequests_SocialGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "SocialGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupJoinRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GroupJoinRequests_GroupId_PendingUserId",
                table: "GroupJoinRequests",
                columns: new[] { "GroupId", "PendingUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupJoinRequests_GroupId_Status_Id",
                table: "GroupJoinRequests",
                columns: new[] { "GroupId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupJoinRequests_GroupId_UserId_Id",
                table: "GroupJoinRequests",
                columns: new[] { "GroupId", "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupJoinRequests_UserId",
                table: "GroupJoinRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OccurredAt_Id",
                table: "OutboxMessages",
                columns: new[] { "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupJoinRequests");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "GroupMembers");
        }
    }
}
