using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Implementations;

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

    public AuthServiceTestContext()
    {
        _connection.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();
        Db.Users.AddRange(
            new ApplicationUser { Id = UserId, UserName = "logout_test", Email = "logout_test@example.com" },
            new ApplicationUser { Id = OtherUserId, UserName = "other_user", Email = "other_user@example.com" });
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
        Service = new AuthService(Mock.Of<IUserRepository>(), NullLogger<AuthService>.Instance,
            IdentityTestFactory.SignInManager().Object, Tokens, repository);
    }

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
