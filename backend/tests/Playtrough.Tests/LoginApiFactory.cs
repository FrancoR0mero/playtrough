using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;

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
    private readonly bool _runPasswordResetWorker;
    private readonly bool _runConfirmationWorker;
    private readonly string _environment;
    private readonly bool _includeSecurityEndpoints;
    public Mock<IEmailConfirmationQueue> ConfirmationQueue { get; } = new();
    internal TestCacheClock CacheClock { get; } = new();
    private readonly Channel<RecoveryEmail> _emails = Channel.CreateUnbounded<RecoveryEmail>();
    public Mock<IEmailSender> EmailSender { get; } = new();
    public Mock<IPasswordResetQueue> ResetQueue { get; } = new();
    public Mock<ILogger<AuthService>> AuthLogger { get; } = new();
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public LoginApiFactory(bool useIndependentConnections = false, bool runPasswordResetWorker = true, bool runConfirmationWorker = true,
        string environment = "Testing", bool includeSecurityEndpoints = false)
    {
        _environment = environment;
        _includeSecurityEndpoints = includeSecurityEndpoints;
        ClientOptions.BaseAddress = new Uri("https://localhost");
        _runConfirmationWorker = runConfirmationWorker;
        _runPasswordResetWorker = runPasswordResetWorker;
        EmailSender.Setup(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((recipient, subject, html, text, _) =>
                _emails.Writer.TryWrite(new RecoveryEmail(recipient, subject, html, text)))
            .Returns(Task.CompletedTask);
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
        builder.UseEnvironment(_environment);
        if (_environment == "Testing")
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://localhost:4200" }));
        builder.ConfigureTestServices(services =>
        {
            // Existing integration suites exercise business behavior without sharing
            // rate budgets. Rate-limiting tests explicitly enable their own limits.
            services.PostConfigure<RateLimitSettings>(settings => settings.Enabled = false);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            // Validate the production options without using a real connection or credentials.
            // The actual database provider below remains isolated SQLite.
            services.PostConfigure<ConnectionStringSettings>(settings =>
                settings.DefaultConnection = "Server=localhost;Database=playtrough_tests;User ID=test_user;");
            // Startup now seeds Identity. Keep the schema ready before Program executes it,
            // and never inherit real admin credentials from user-secrets or the environment.
            services.PostConfigure<AdminSeedSettings>(settings =>
                settings.Username = settings.Email = settings.Password = string.Empty);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(EmailSender.Object);
            services.AddSingleton(AuthLogger.Object);
            services.Configure<MemoryCacheOptions>(options => options.Clock = CacheClock);
            if (!_runConfirmationWorker)
            {
                services.Remove(services.Single(service => service.ServiceType == typeof(IHostedService) &&
                    service.ImplementationType == typeof(EmailConfirmationWorker)));
                services.RemoveAll<IEmailConfirmationQueue>();
                services.AddSingleton(ConfirmationQueue.Object);
            }
            if (!_runPasswordResetWorker)
            {
                var worker = services.Single(service => service.ServiceType == typeof(IHostedService) &&
                    service.ImplementationType == typeof(PasswordResetWorker));
                services.Remove(worker);
                services.RemoveAll<IPasswordResetQueue>();
                services.AddSingleton(ResetQueue.Object);
            }
            services.PostConfigure<EmailSettings>(settings =>
            {
                settings.Host = "localhost";
                settings.Port = 1025;
                settings.FromAddress = "no-reply@playtrough.local";
                settings.SecureSocketMode = EmailSecurity.None;
                settings.Username = settings.Password = string.Empty;
            });
            services.PostConfigure<PasswordResetSettings>(settings => settings.FrontendBaseUrl = "http://localhost:4200");
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
            {
                if (_connectionString is null) options.UseSqlite(_connection);
                else options.UseSqlite(_connectionString);
            });
            using (var schema = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection).Options))
                schema.Database.EnsureCreated();

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
            if (_includeSecurityEndpoints)
                services.AddControllers().ConfigureApplicationPartManager(manager =>
                    manager.FeatureProviders.Add(new SecurityControllerFeatureProvider()));
        });
    }

    public async Task CreateUserAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Username, Email = Email, EmailConfirmed = true };
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

    public Task<RecoveryEmail> ReadEmailAsync()
        => _emails.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

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

internal sealed class TestCacheClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}
