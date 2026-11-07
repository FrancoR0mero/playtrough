using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Playtrough.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameBrainIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameBrainId",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IgdbId",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyMetadataJson",
                table: "Games",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataExpiresAt",
                table: "Games",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Games",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SteamId",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_GameBrainId",
                table: "Games",
                column: "GameBrainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_MetadataExpiresAt",
                table: "Games",
                column: "MetadataExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_GameBrainId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_MetadataExpiresAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "GameBrainId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "IgdbId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LegacyMetadataJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MetadataExpiresAt",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SteamId",
                table: "Games");
        }
    }
}
