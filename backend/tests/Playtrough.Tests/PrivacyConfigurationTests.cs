using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Migrations;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class PrivacyConfigurationTests
{
    [Theory]
    [InlineData(Visibility.Public, Visibility.Friends)]
    [InlineData(Visibility.Private, Visibility.Public)]
    public async Task RegistrationAndAdminSeeding_UseConfiguredDefaults(Visibility profile, Visibility history)
    {
        using var c = new GamesApiTestContext();
        using var factory = c.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<ProfileSettings>(settings =>
            {
                settings.DefaultProfileVisibility = profile;
                settings.DefaultHistoryVisibility = history;
            });
            services.PostConfigure<AdminSeedSettings>(settings =>
            {
                settings.Username = "privacy_admin";
                settings.Email = "privacy_admin@example.com";
                settings.Password = LoginApiFactory.Password;
            });
        }));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = "privacy_new", email = "privacy_new@example.com", password = LoginApiFactory.Password,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, u =>
        {
            Assert.Equal(profile, u.ProfileVisibility);
            Assert.Equal(history, u.HistoryVisibility);
        });
    }

    [Fact]
    public async Task Migration_BackfillsExistingUsersWithPublicProfileAndFriendsHistory()
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb(false);
        // Reconstruct the immediately preceding schema in this isolated test DB.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN ProfileVisibility;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users DROP COLUMN HistoryVisibility;");
        var migration = new AddUserPrivacy { ActiveProvider = db.Database.ProviderName! };
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model);
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var users = await db.Users.AsNoTracking().ToListAsync();
        Assert.Equal(60, users.Count);
        Assert.All(users, user =>
        {
            Assert.Equal(Visibility.Public, user.ProfileVisibility);
            Assert.Equal(Visibility.Friends, user.HistoryVisibility);
        });
    }

    [Fact]
    public void MySqlMigration_HasStableDefaultsAndNoPendingModelChanges()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261108000454_AddFriendships", "20261108005013_AddUserPrivacy");
        Assert.Contains("ADD `ProfileVisibility` int NOT NULL DEFAULT 0", sql);
        Assert.Contains("ADD `HistoryVisibility` int NOT NULL DEFAULT 1", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("ALTER TABLE `Reviews`", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task PrivacyUpdate_ProtectsAgainstStaleIdentityProfileWrite()
    {
        using var c = new GamesApiTestContext();
        using var client = await c.AuthenticatedClientAsync();
        using var scope = c.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stale = (await users.FindByNameAsync("games_user_1"))!;
        await PrivacyEndpointsTests.Set(client, "private", "public");
        stale.Bio = "stale update";
        var update = await users.UpdateAsync(stale);
        Assert.False(update.Succeeded);
        Assert.Contains(update.Errors, error => error.Code == "ConcurrencyFailure");
        Assert.Equal(new PrivacyDto("private", "public"), await client.GetFromJsonAsync<PrivacyDto>("/api/users/me/privacy"));
    }

    [Fact]
    public async Task Swagger_DocumentsPrivacyValuesAndRestrictedShape()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var root = doc.RootElement;
        var paths = root.GetProperty("paths");
        var put = paths.GetProperty("/api/users/me/privacy").GetProperty("put");
        Assert.Contains("profile-write", put.GetProperty("description").GetString());
        Assert.Contains("invalid_visibility", put.GetProperty("responses").GetProperty("400").GetProperty("description").GetString());
        Assert.Contains("profile_restricted", paths.GetProperty("/api/users/{username}/friends").GetProperty("get")
            .GetProperty("responses").GetProperty("403").GetProperty("description").GetString());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("UpdatePrivacyRequest");
        Assert.Equal(2, request.GetProperty("required").GetArrayLength());
        foreach (var property in request.GetProperty("properties").EnumerateObject())
            Assert.Equal(new[] { "public", "friends", "private" }, property.Value.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(new[] { "avatarUrl", "isRestricted", "relationship", "restriction", "username" },
            schemas.GetProperty("RestrictedProfileDto").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Contains("bio", schemas.GetProperty("ProfileDto").GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        Assert.False(schemas.GetProperty("RestrictedProfileDto").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(2, paths.GetProperty("/api/users/{username}").GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("oneOf").GetArrayLength());
    }
}
