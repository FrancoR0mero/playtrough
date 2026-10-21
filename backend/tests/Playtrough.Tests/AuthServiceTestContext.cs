using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

/// <summary>Service-level fixture: real hashing and relational persistence, with Identity mocked.
/// SQLite supports ExecuteUpdateAsync; EF InMemory cannot verify the actual revocation update.</summary>
internal sealed class AuthServiceTestContext : IDisposable
{
    public const int UserId = 42;
    public const int OtherUserId = 43;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public AppDbContext Db { get; }
    public TokenService Tokens { get; }
    public AuthService Service { get; }
    public Mock<IUserRepository> Users { get; } = new();
    public Mock<SignInManager<ApplicationUser>> SignIn { get; } = IdentityTestFactory.SignInManager();
    public Mock<ILogger<AuthService>> Logger { get; } = new();
    public IList<string> Roles { get; set; } = new List<string> { "User" };

    public AuthServiceTestContext()
    {
        _connection.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();
        Db.Users.AddRange(
            new ApplicationUser { Id = UserId, UserName = "logout_test", EmailConfirmed = true, Email = "logout_test@example.com" },
            new ApplicationUser { Id = OtherUserId, UserName = "other_user", EmailConfirmed = true, Email = "other_user@example.com" });
        Db.SaveChanges();

        var repository = new RefreshTokenRepository(Db);
        Tokens = new TokenService(Options.Create(new JwtSettings
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            Issuer = "Playtrough.Tests",
            Audience = "Playtrough.Tests.Client",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
        }), repository);
        Users.Setup(users => users.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => Db.Database.BeginTransactionAsync(ct));
        Users.Setup(users => users.FindByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken ct) => Db.Users.SingleOrDefaultAsync(user => user.Id == id, ct));
        Users.Setup(users => users.GetRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Roles);
        SignIn.Setup(signIn => signIn.CanSignInAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(true);
        Mock.Get(SignIn.Object.UserManager).Setup(users => users.IsLockedOutAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(false);
        Service = CreateService();
    }

    public AuthService CreateService(ITokenService? tokens = null, IRefreshTokenRepository? repository = null)
        => new(Users.Object, Logger.Object, SignIn.Object, tokens ?? Tokens,
            repository ?? new RefreshTokenRepository(Db), Mock.Of<IEmailSender>(), Mock.Of<IPasswordResetQueue>(),
            IdentityTestFactory.PasswordResetOptions(),
            Microsoft.Extensions.Options.Options.Create(new EmailConfirmationSettings()),
            Mock.Of<IEmailConfirmationQueue>(), new Microsoft.Extensions.Caching.Memory.MemoryCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

    public async Task<string> CreateRefreshTokenAsync(int userId = UserId)
    {
        var user = await Db.Users.SingleAsync(user => user.Id == userId);
        var tokens = await Tokens.CreateAsync(user, ["User"]);
        return tokens.RefreshToken;
    }

    public Task<RefreshToken> ReadRefreshTokenAsync()
        => Db.RefreshTokens.AsNoTracking().SingleAsync();

    public Task LogoutAsync(string refreshToken, int userId = UserId)
        => Service.LogoutAsync(userId, new LogoutRequestDto { RefreshToken = refreshToken });

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
