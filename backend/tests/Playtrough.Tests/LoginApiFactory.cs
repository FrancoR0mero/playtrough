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

    private readonly SqliteConnection _connection;
    private readonly string? _databasePath;
    private readonly string? _connectionString;
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public LoginApiFactory(bool useIndependentConnections = false)
    {
        if (useIndependentConnections)
        {
            // Concurrency needs separate connections. A temporary WAL database lets SQLite
            // wait for a writer instead of sharing one connection/transaction between requests.
            _databasePath = Path.Combine(Path.GetTempPath(), $"playtrough-refresh-{Guid.NewGuid():N}.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath, Pooling = false, DefaultTimeout = 10,
            }.ToString();
        }
        _connection = new SqliteConnection(_connectionString ?? "Data Source=:memory:");
        // The database lives until this connection is disposed, across HTTP request scopes.
        _connection.Open();
        if (_databasePath is not null)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteNonQuery();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
            {
                if (_connectionString is null) options.UseSqlite(_connection);
                else options.UseSqlite(_connectionString);
            });

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
        if (disposing)
        {
            _connection.Dispose();
            if (_databasePath is not null)
            {
                File.Delete(_databasePath);
                File.Delete(_databasePath + "-wal");
                File.Delete(_databasePath + "-shm");
            }
        }
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
