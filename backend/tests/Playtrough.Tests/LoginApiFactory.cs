using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Tests;

/// <summary>Runs the real API with one isolated relational database per factory.
/// Authentication, Identity, repositories and token generation are never replaced.</summary>
public sealed class LoginApiFactory : WebApplicationFactory<Program>
{
    public const string Username = "login_test";
    public const string Email = "login_test@example.com";
    public const string Password = "Str0ngTest123!";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public LoginApiFactory()
    {
        // The database lives until this connection is disposed, across HTTP request scopes.
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // Keep the production JWT validation configuration, with a fresh test-only key.
            services.PostConfigure<JwtSettings>(settings =>
            {
                settings.Key = SigningKey;
                settings.Issuer = "Playtrough.Tests";
                settings.Audience = "Playtrough.Tests.Client";
                settings.AccessTokenExpirationMinutes = 15;
                settings.RefreshTokenExpirationDays = 7;
            });

            // This assembly is never loaded as an application part by the production API.
            services.AddControllers().AddApplicationPart(typeof(TestProtectedController).Assembly);
        });
    }

    public async Task CreateUserAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Username, Email = Email };
        RequireSuccess(await users.CreateAsync(user, Password));
        RequireSuccess(await users.AddToRoleAsync(user, "User"));
    }

    public async Task<ApplicationUser> ReadUserAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .AsNoTracking().SingleAsync(user => user.UserName == Username);
    }

    public async Task<List<RefreshToken>> ReadRefreshTokensAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens
            .AsNoTracking().ToListAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
