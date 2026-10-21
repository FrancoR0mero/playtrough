using Microsoft.EntityFrameworkCore.Migrations;

namespace Playtrough.Api.Migrations;

public partial class ConfirmExistingUserEmails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("UPDATE `Users` SET `EmailConfirmed` = 1 WHERE `EmailConfirmed` = 0;");

    // Data-only change: never undo confirmations, including ones performed by users.
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
