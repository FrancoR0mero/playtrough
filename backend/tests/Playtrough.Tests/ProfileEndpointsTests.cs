using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class ProfileEndpointsTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task Profiles_ExposeCorrectFields_AndHideUnconfirmedAccountsExceptOwner(string role)
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync(role);
        using var other = await context.AuthenticatedClientAsync();
        var mine = (await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!;
        Assert.Equal("games_user_1", mine.Username);
        Assert.Equal("games_user_1@example.com", mine.Email);
        Assert.Null(mine.AvatarUrl);
        Assert.Null(mine.Bio);
        Assert.Null(mine.UsernameChangeAvailableAt);
        Assert.Equal(0, mine.FriendsCount);
        Assert.Equal(0, mine.PendingReceivedCount);
        Assert.Equal(DateTimeKind.Utc, mine.CreatedAt.Kind);
        using var profile = (await other.GetFromJsonAsync<JsonDocument>("/api/users/GAMES_USER_1"))!;
        var root = profile.RootElement;
        Assert.Equal(new[] { "avatarUrl", "bio", "createdAt", "friendsCount", "isRestricted", "relationship", "restriction", "username" },
            root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("none", root.GetProperty("relationship").GetProperty("status").GetString());
        Assert.False(root.GetProperty("relationship").TryGetProperty("requestId", out _));
        Assert.False(root.GetProperty("isRestricted").GetBoolean());
        using (var scope = context.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == mine.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.EmailConfirmed, false));
        await Error(await other.GetAsync("/api/users/games_user_1"), 404, "user_not_found");
        await Error(await other.GetAsync("/api/users/does_not_exist"), 404, "user_not_found");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/users/GAMES_USER_1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/users/search")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/users/me")]
    [InlineData("GET", "/api/users/someone")]
    [InlineData("PUT", "/api/users/me")]
    public async Task NoToken_Returns401(string method, string path)
    {
        using var context = new GamesApiTestContext();
        using var client = context.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT") request.Content = JsonContent.Create(new { bio = "test" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstu")]
    [InlineData("with.dot")]
    [InlineData("with space")]
    [InlineData("with-dash")]
    [InlineData("name\n")]
    [InlineData("ñame")]
    [InlineData(123)]
    [InlineData(null)]
    public async Task InvalidUsername_IsRejectedWithoutChangingBio(object? username)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        await Error(await client.PutAsJsonAsync("/api/users/me", new { username, bio = "must not persist" }), 400, "invalid_username");
        Assert.Null((await client.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.Bio);
    }

    [Theory]
    [InlineData("me")]
    [InlineData("search")]
    [InlineData("ADMIN")]
    [InlineData("administrator")]
    [InlineData("moderator")]
    [InlineData("support")]
    [InlineData("system")]
    [InlineData("api")]
    [InlineData("null")]
    [InlineData("undefined")]
    [InlineData("root")]
    public async Task ReservedNames_RejectedByRegistrationAndProfile(string username)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        await Error(await client.PutAsJsonAsync("/api/users/me", new { username }), 400, "username_reserved");
        await Error(await client.PostAsJsonAsync("/api/auth/register", new
        {
            username, email = "reserved@example.com", password = LoginApiFactory.Password,
        }), 400, "username_reserved");
    }

    [Fact]
    public async Task ConfiguredReservations_UsedByBothEndpoints()
    {
        using var context = new GamesApiTestContext();
        using var original = await context.AuthenticatedClientAsync();
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<ProfileSettings>(settings => settings.ReservedUsernames = ["custom_name"])));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        await Error(await client.PutAsJsonAsync("/api/users/me", new { username = "CUSTOM_NAME" }), 400, "username_reserved");
        await Error(await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = "custom_name", email = "reserved@example.com", password = LoginApiFactory.Password,
        }), 400, "username_reserved");
    }

    [Theory]
    [InlineData("games_user_2")]
    [InlineData("GAMES_USER_2")]
    public async Task TakenUsername_IsCaseInsensitive(string username)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        await Error(await client.PutAsJsonAsync("/api/users/me", new { username }), 409, "username_taken");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(2)]
    public async Task Rename_CooldownHasExactBoundary_AndCaseOnlyIsNoOp(int days)
    {
        using var context = new GamesApiTestContext();
        using var original = await context.AuthenticatedClientAsync();
        var clock = new CatalogPersistenceClock { UtcNow = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero) };
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.PostConfigure<ProfileSettings>(settings => settings.UsernameChangeCooldownDays = days);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        var unchanged = await Put(client, new { username = "GAMES_USER_1" });
        Assert.Null(unchanged.UsernameChangeAvailableAt);
        Assert.Equal("games_user_1", unchanged.Username);
        var first = await Put(client, new { username = "New_Name123", bio = "kept" });
        var available = clock.UtcNow.UtcDateTime.AddDays(days);
        Assert.Equal(available, first.UsernameChangeAvailableAt);
        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == first.Id);
            Assert.Equal("NEW_NAME123", user.NormalizedUserName);
            Assert.Equal(clock.UtcNow.UtcDateTime, user.UsernameChangedAt);
        }
        Assert.Equal(first, await Put(client, new { username = "new_name123" }));
        clock.UtcNow = new DateTimeOffset(available.AddTicks(-1));
        using var blocked = await client.PutAsJsonAsync("/api/users/me", new { username = "another_name", bio = "discarded" });
        var error = await Error(blocked, 409, "username_change_too_soon");
        Assert.Equal(available, error.GetProperty("availableAt").GetDateTime());
        Assert.Equal("kept", (await client.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.Bio);
        clock.UtcNow = new DateTimeOffset(available);
        Assert.Null((await client.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.UsernameChangeAvailableAt);
        Assert.Equal("another_name", (await Put(client, new { username = "another_name" })).Username);
    }

    [Theory]
    [InlineData("  first\r\nsecond\rthird\0\u0001\t🎮  ", "first\nsecond\nthird\t🎮")]
    [InlineData(" \r\n\t ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("<b>plain text</b>", "<b>plain text</b>")]
    public async Task Bio_Sanitized_AndPartialUpdatesPreserveOmittedFields(string? bio, string? expected)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var edited = await Put(client, new { bio });
        Assert.Equal(expected, edited.Bio);
        Assert.Equal("games_user_1", edited.Username);
        Assert.Null(edited.UsernameChangeAvailableAt);
        Assert.Equal(edited, await Put(client, new { }));
        Assert.Equal(expected, (await Put(client, new { username = "renamed_user" })).Bio);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("🎮")]
    public async Task Bio_LengthCountsUnicodeScalars_AfterSanitizing(string character)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var bio = string.Concat(Enumerable.Repeat(character, 300));
        Assert.Equal(bio, (await Put(client, new { bio = "  " + bio + "\0 " })).Bio);
        await Error(await client.PutAsJsonAsync("/api/users/me", new { bio = bio + character }), 400, "invalid_bio");
        Assert.Equal(bio, (await client.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.Bio);
        Assert.Null((await Put(client, new { bio = (string?)null })).Bio);
    }

    [Theory]
    [InlineData("{\"bio\":42}", "invalid_bio")]
    [InlineData("{\"bio\":{}}", "invalid_bio")]
    [InlineData("{\"bio\":[]}", "invalid_bio")]
    [InlineData("{\"username\":true}", "invalid_username")]
    [InlineData("null", "invalid_username")]
    [InlineData("{", "invalid_username")]
    public async Task InvalidJsonTypes_UseStableCodes(string json, string code)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        await Error(await client.PutAsync("/api/users/me", new StringContent(json, Encoding.UTF8, "application/json")), 400, code);
    }

    [Fact]
    public async Task Refresh_AfterRename_ReissuesJwtFromDatabase()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = "games_user_1", password = LoginApiFactory.Password,
        });
        var session = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        await Put(client, new { username = "new_session_name" });
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var renewed = (await refresh.Content.ReadFromJsonAsync<RefreshResponseDto>())!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(renewed.AccessToken);
        Assert.Equal("new_session_name", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.UniqueName).Value);
        Assert.Equal("new_session_name", renewed.User.Username);
        Assert.NotEqual(session.RefreshToken, renewed.RefreshToken);
    }

    [Fact]
    public async Task WriteRateLimit_TenPerUser_Then429WithRetryAfter_ReadsRemainAvailable()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var client = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync();
        for (var i = 0; i < 10; i++) await Put(client, new { bio = i.ToString() });
        using var limited = await client.PutAsJsonAsync("/api/users/me", new { bio = "blocked" });
        await Error(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("9", (await client.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.Bio);
        await Put(other, new { bio = "other budget" });
        using var renewed = await context.LoginClientAsync();
        await Error(await renewed.PutAsJsonAsync("/api/users/me", new { }), 429, "rate_limited");
    }

    internal static async Task<MyProfileDto> Put(HttpClient client, object body)
    {
        using var response = await client.PutAsJsonAsync("/api/users/me", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MyProfileDto>())!;
    }
    internal static async Task<JsonElement> Error(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, json.GetProperty("code").GetString());
        return json;
    }
}
