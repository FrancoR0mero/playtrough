using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Authorization;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class IdentitySeederTests
{
    [Fact]
    public async Task ApplicationStartup_WithConfiguredAdmin_RunsSeederWithoutSendingEmail()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        var settings = TestSettings();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<AdminSeedSettings>(options =>
            {
                options.Username = settings.Username;
                options.Email = settings.Email;
                options.Password = settings.Password;
            })));

        // Act
        using var client = configured.CreateClient();

        // Assert
        using var scope = configured.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(settings.Email))!;
        Assert.NotNull(admin);
        Assert.True(admin.EmailConfirmed);
        Assert.Equal(AppRoles.Admin, Assert.Single(await users.GetRolesAsync(admin)));
        Assert.Empty(factory.EmailSender.Invocations);
    }

    private static AdminSeedSettings TestSettings() => new()
    {
        Username = "seed_admin_test", Email = "seed_admin_test@example.com",
        Password = $"SeedTest1!{Guid.NewGuid():N}",
    };

    private static IdentitySeeder Seeder(IServiceProvider services, AdminSeedSettings settings,
        Mock<ILogger<IdentitySeeder>>? logger = null) => new(
        services.GetRequiredService<AppDbContext>(),
        services.GetRequiredService<RoleManager<IdentityRole<int>>>(),
        services.GetRequiredService<UserManager<ApplicationUser>>(),
        Options.Create(settings), (logger ?? new Mock<ILogger<IdentitySeeder>>()).Object);

    [Fact]
    public async Task RoleSeed_WhenRunTwice_CreatesOnlyMissingRoleAndPreservesExistingSeed()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var db = services.GetRequiredService<AppDbContext>();
        var existingUser = (await roles.FindByNameAsync(AppRoles.User))!;
        var existingAdmin = (await roles.FindByNameAsync(AppRoles.Admin))!;
        var stamp = existingAdmin.ConcurrencyStamp;
        Assert.True((await roles.DeleteAsync((await roles.FindByNameAsync(AppRoles.Moderator))!)).Succeeded);
        var seeder = Seeder(services, new AdminSeedSettings());

        // Act
        await seeder.InitializeAsync();
        await seeder.InitializeAsync();

        // Assert
        Assert.Equal(3, await db.Roles.CountAsync());
        Assert.Equal(AppRoles.All.OrderBy(role => role), await db.Roles.Select(role => role.Name!).OrderBy(role => role).ToListAsync());
        Assert.Equal(1, existingUser.Id);
        Assert.Equal(2, existingAdmin.Id);
        Assert.Equal(stamp, (await roles.FindByNameAsync(AppRoles.Admin))!.ConcurrencyStamp);
    }

    [Fact]
    public async Task AdminSeed_WithConfiguration_CreatesConfirmedAdminAndIsIdempotent()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var settings = TestSettings();
        var seeder = Seeder(services, settings);

        // Act
        await seeder.InitializeAsync();
        var first = (await users.FindByEmailAsync(settings.Email))!;
        var stamp = first.SecurityStamp;
        await seeder.InitializeAsync();

        // Assert
        var admin = (await users.FindByNameAsync(settings.Username))!;
        Assert.True(admin.EmailConfirmed);
        Assert.True(await users.CheckPasswordAsync(admin, settings.Password));
        Assert.Equal(AppRoles.Admin, Assert.Single(await users.GetRolesAsync(admin)));
        Assert.Equal(stamp, admin.SecurityStamp);
        Assert.Equal(1, await services.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData("all")]
    [InlineData("username")]
    [InlineData("email")]
    [InlineData("password")]
    public async Task AdminSeed_WithAbsentOrIncompleteConfiguration_DoesNotCreateAccountAndLogsSafeWarning(string missing)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var scope = factory.Services.CreateScope();
        var settings = TestSettings();
        if (missing == "all") settings = new AdminSeedSettings();
        if (missing == "username") settings.Username = " ";
        if (missing == "email") settings.Email = " ";
        if (missing == "password") settings.Password = " ";
        var logger = new Mock<ILogger<IdentitySeeder>>();

        // Act
        await Seeder(scope.ServiceProvider, settings, logger).InitializeAsync();

        // Assert
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.ToListAsync());
        var log = Assert.Single(logger.Invocations, call => call.Method.Name == "Log");
        Assert.Equal(LogLevel.Warning, log.Arguments[0]);
        Assert.DoesNotContain("seed_admin_test", log.Arguments[2]?.ToString());
        if (!string.IsNullOrWhiteSpace(settings.Password))
            Assert.DoesNotContain(settings.Password, log.Arguments[2]?.ToString());
    }

    [Theory]
    [InlineData("both")]
    [InlineData("username")]
    [InlineData("email")]
    public async Task AdminSeed_WithExistingMatchingAccount_DoesNotPromoteOrModifyIt(string match)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var settings = TestSettings();
        var user = new ApplicationUser
        {
            UserName = match == "email" ? "existing_user" : settings.Username,
            Email = match == "username" ? "existing_user@example.com" : settings.Email,
        };
        Assert.True((await users.CreateAsync(user, LoginApiFactory.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, AppRoles.User)).Succeeded);
        var hash = user.PasswordHash;
        var stamp = user.SecurityStamp;
        var concurrency = user.ConcurrencyStamp;

        // Act
        await Seeder(services, settings).InitializeAsync();

        // Assert
        await services.GetRequiredService<AppDbContext>().Entry(user).ReloadAsync();
        Assert.False(user.EmailConfirmed);
        Assert.Equal(hash, user.PasswordHash);
        Assert.Equal(stamp, user.SecurityStamp);
        Assert.Equal(concurrency, user.ConcurrencyStamp);
        Assert.Equal(AppRoles.User, Assert.Single(await users.GetRolesAsync(user)));
        Assert.Equal(1, await services.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task AdminSeed_WithInvalidPassword_RollsBackAndDoesNotExposeConfiguration()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var scope = factory.Services.CreateScope();
        var settings = TestSettings();
        settings.Password = "short";

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(scope.ServiceProvider, settings).InitializeAsync());

        // Assert
        Assert.DoesNotContain(settings.Username, error.Message);
        Assert.DoesNotContain(settings.Email, error.Message);
        Assert.DoesNotContain(settings.Password, error.Message);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().ToListAsync());
    }
}
