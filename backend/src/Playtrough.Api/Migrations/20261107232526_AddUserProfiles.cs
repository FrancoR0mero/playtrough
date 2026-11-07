using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Users",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "UsernameChangedAt",
                table: "Users",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UsernameChangedAt",
                table: "Users");
        }
    }
}
