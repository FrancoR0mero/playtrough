using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Playtrough.Tests;

public class AuthServiceLogoutTests
{
    [Fact]
    public async Task Logout_WithActiveOwnedToken_PersistsRevokedAt()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        Assert.Null((await context.ReadRefreshTokenAsync()).RevokedAt);
        var before = DateTime.UtcNow;

        // Act
        await context.LogoutAsync(token);

        // Assert: read without tracking, since ExecuteUpdateAsync bypasses tracked entities.
        var stored = await context.ReadRefreshTokenAsync();
        Assert.NotNull(stored.RevokedAt);
        Assert.InRange(stored.RevokedAt.Value, before, DateTime.UtcNow);
        Assert.False(stored.IsActive(DateTime.UtcNow));
        Assert.Equal(context.Tokens.HashRefreshToken(token), stored.TokenHash);
    }

    [Fact]
    public async Task Logout_WithPreviouslyRevokedToken_PreservesRevokedAt()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        await context.LogoutAsync(token);
        var originalRevocation = (await context.ReadRefreshTokenAsync()).RevokedAt;
        Assert.NotNull(originalRevocation);

        // Act
        await context.LogoutAsync(token);

        // Assert
        Assert.Equal(originalRevocation, (await context.ReadRefreshTokenAsync()).RevokedAt);
    }

    [Fact]
    public async Task Logout_WithNonexistentToken_CompletesWithoutRevokingExistingToken()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        await context.CreateRefreshTokenAsync();

        // Act
        var error = await Record.ExceptionAsync(() => context.LogoutAsync("nonexistent_refresh_token"));

        // Assert
        Assert.Null(error);
        Assert.Null((await context.ReadRefreshTokenAsync()).RevokedAt);
    }

    [Fact]
    public async Task Logout_WithAnotherUsersToken_MatchesNonexistentTokenAndDoesNotRevoke()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync(AuthServiceTestContext.OtherUserId);

        // Act
        var foreignTokenError = await Record.ExceptionAsync(() => context.LogoutAsync(token));
        var missingTokenError = await Record.ExceptionAsync(() => context.LogoutAsync("nonexistent_refresh_token"));

        // Assert: LogoutAsync returns Task, so both expected cases complete without an error/result.
        Assert.Null(foreignTokenError);
        Assert.Null(missingTokenError);
        var stored = await context.ReadRefreshTokenAsync();
        Assert.Equal(AuthServiceTestContext.OtherUserId, stored.UserId);
        Assert.Null(stored.RevokedAt);
        Assert.True(stored.IsActive(DateTime.UtcNow));
    }

    [Fact]
    public async Task Logout_WithExpiredToken_CompletesWithoutRevoking()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        var expired = await context.Db.RefreshTokens.SingleAsync();
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await context.Db.SaveChangesAsync();

        // Act
        var error = await Record.ExceptionAsync(() => context.LogoutAsync(token));

        // Assert
        Assert.Null(error);
        var stored = await context.ReadRefreshTokenAsync();
        Assert.Null(stored.RevokedAt);
        Assert.Equal(expired.ExpiresAt, stored.ExpiresAt);
        Assert.False(stored.IsActive(DateTime.UtcNow));
    }
}
