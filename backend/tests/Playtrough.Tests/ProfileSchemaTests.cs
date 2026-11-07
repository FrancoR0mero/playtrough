using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class ProfileSchemaTests
{
    [Fact]
    public void Migration_OnlyAddsNullableProfileColumns_WithUtf8mb4()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261104191806_AddGameBrainDetailsCache", "20261107232526_AddUserProfiles");
        Assert.Contains("ADD `Bio` varchar(300) CHARACTER SET utf8mb4 NULL", sql);
        Assert.Contains("ADD `UsernameChangedAt` datetime(6) NULL", sql);
        Assert.DoesNotContain("UPDATE `Users`", sql);
        Assert.DoesNotContain("ALTER TABLE `Games`", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Swagger_DescribesPartialFields_Errors_AndLiteralRoutes()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/users/{username}", out var profile));
        Assert.Equal("Users", profile.GetProperty("get").GetProperty("tags")[0].GetString());
        var update = paths.GetProperty("/api/users/me").GetProperty("put");
        var responses = update.GetProperty("responses");
        foreach (var code in new[] { "200", "400", "401", "404", "409", "429" }) Assert.True(responses.TryGetProperty(code, out _));
        Assert.Contains("username_reserved", responses.GetProperty("400").GetProperty("description").GetString());
        Assert.Contains("availableAt", responses.GetProperty("409").GetProperty("description").GetString());
        Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        var request = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("UpdateProfileRequest");
        Assert.False(request.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);
        Assert.Equal(300, request.GetProperty("properties").GetProperty("bio").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public async Task LegacyReservedAccount_CanKeepItsName_AndEditBio()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using (var scope = context.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.UserName == "games_user_1").ExecuteUpdateAsync(update =>
                update.SetProperty(u => u.UserName, "admin").SetProperty(u => u.NormalizedUserName, "ADMIN"));
        }
        var profile = await ProfileEndpointsTests.Put(client, new { username = "ADMIN", bio = "existing account" });
        Assert.Equal("admin", profile.Username);
        Assert.Equal("existing account", profile.Bio);
        Assert.Null(profile.UsernameChangeAvailableAt);
    }
}
