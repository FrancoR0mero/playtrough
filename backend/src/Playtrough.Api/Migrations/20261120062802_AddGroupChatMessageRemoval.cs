using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupChatMessageRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "GroupMessages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemovedByUserId",
                table: "GroupMessages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMessages_RemovedByUserId",
                table: "GroupMessages",
                column: "RemovedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMessages_Users_RemovedByUserId",
                table: "GroupMessages",
                column: "RemovedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMessages_Users_RemovedByUserId",
                table: "GroupMessages");

            migrationBuilder.DropIndex(
                name: "IX_GroupMessages_RemovedByUserId",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "RemovedByUserId",
                table: "GroupMessages");
        }
    }
}
