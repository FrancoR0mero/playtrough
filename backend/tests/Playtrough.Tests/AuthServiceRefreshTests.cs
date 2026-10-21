using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class AuthServiceRefreshTests
{
    [Fact]
    public async Task Refresh_WithActiveToken_RotatesAndPersistsOnlyHashForSevenDays()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        var before = DateTime.UtcNow;

        // Act
        var result = await context.Service.RefreshAsync(Request(token));

        // Assert
        Assert.True(result.Success);
        var response = Assert.IsType<RefreshResponseDto>(result.Value);
        Assert.NotEqual(token, response.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        var stored = await context.Db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(2, stored.Count);
        var original = Assert.Single(stored, item => item.TokenHash == context.Tokens.HashRefreshToken(token));
        var replacement = Assert.Single(stored, item => item.TokenHash == context.Tokens.HashRefreshToken(response.RefreshToken));
        Assert.NotNull(original.RevokedAt);
        Assert.InRange(original.RevokedAt.Value, before, DateTime.UtcNow);
        Assert.Equal(replacement.TokenHash, original.ReplacedByToken);
        Assert.NotEqual(response.RefreshToken, replacement.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), replacement.ExpiresAt - replacement.CreatedAt);
        Assert.InRange(replacement.CreatedAt, before, DateTime.UtcNow);
        Assert.True(replacement.IsActive(DateTime.UtcNow));
        Assert.Null(replacement.ReplacedByToken);
        Assert.Equal(AuthServiceTestContext.UserId, response.User.Id);
        Assert.Equal("User", response.User.Role);
        Assert.InRange(response.ExpiresAt, before.AddMinutes(15), DateTime.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Refresh_WithNonexistentToken_ReturnsInvalidRefreshToken()
    {
        // Arrange
        using var context = new AuthServiceTestContext();

        // Act
        var result = await context.Service.RefreshAsync(Request("nonexistent"));

        // Assert
        AssertInvalid(result);
        Assert.Empty(await context.Db.RefreshTokens.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsInvalidRefreshTokenWithoutRevoking()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        var expired = await context.Db.RefreshTokens.SingleAsync();
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await context.Db.SaveChangesAsync();

        // Act
        var result = await context.Service.RefreshAsync(Request(token));

        // Assert
        AssertInvalid(result);
        var stored = await context.ReadRefreshTokenAsync();
        Assert.Null(stored.RevokedAt);
        Assert.Null(stored.ReplacedByToken);
    }

    [Fact]
    public async Task Refresh_WithLogoutRevokedToken_DoesNotRevokeOtherSessions()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        var otherSession = await context.CreateRefreshTokenAsync();
        await context.LogoutAsync(token);

        // Act
        var result = await context.Service.RefreshAsync(Request(token));

        // Assert
        AssertInvalid(result);
        var other = await context.Db.RefreshTokens.AsNoTracking()
            .SingleAsync(item => item.TokenHash == context.Tokens.HashRefreshToken(otherSession));
        Assert.True(other.IsActive(DateTime.UtcNow));
        Assert.DoesNotContain(context.Logger.Invocations, call => call.Method.Name == "Log" &&
            call.Arguments[0] is LogLevel.Warning);
    }

    [Fact]
    public async Task Refresh_WithReusedToken_RevokesAllActiveTokensForThatUserOnly()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        await context.CreateRefreshTokenAsync();
        var foreign = await context.CreateRefreshTokenAsync(AuthServiceTestContext.OtherUserId);
        var rotated = await context.Service.RefreshAsync(Request(token));
        Assert.True(rotated.Success);

        // Act
        var result = await context.Service.RefreshAsync(Request(token));

        // Assert
        AssertInvalid(result);
        var stored = await context.Db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.All(stored.Where(item => item.UserId == AuthServiceTestContext.UserId), item => Assert.NotNull(item.RevokedAt));
        Assert.True(Assert.Single(stored, item => item.TokenHash == context.Tokens.HashRefreshToken(foreign)).IsActive(DateTime.UtcNow));
        AssertInvalid(await context.Service.RefreshAsync(Request(rotated.Value!.RefreshToken)));
        var warning = Assert.Single(context.Logger.Invocations, call => call.Method.Name == "Log" &&
            call.Arguments[0] is LogLevel.Warning);
        Assert.NotNull(warning);
        var message = Assert.IsType<string>(warning.Arguments[2]?.ToString());
        Assert.Contains("Refresh token reuse detected", message);
        Assert.DoesNotContain(token, message);
        Assert.DoesNotContain(context.Tokens.HashRefreshToken(token), message);
        Assert.DoesNotContain(rotated.Value.RefreshToken, message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("not-allowed")]
    public async Task Refresh_WithUnavailableUser_ReturnsInvalidRefreshTokenAndRollsBack(string unavailable)
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        if (unavailable == "missing")
            context.Users.Setup(users => users.FindByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApplicationUser?)null);
        else if (unavailable == "locked")
            Mock.Get(context.SignIn.Object.UserManager).Setup(users => users.IsLockedOutAsync(It.IsAny<ApplicationUser>()))
                .ReturnsAsync(true);
        else
            context.SignIn.Setup(signIn => signIn.CanSignInAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(false);

        // Act
        var result = await context.Service.RefreshAsync(Request(token));

        // Assert
        AssertInvalid(result);
        var stored = await context.ReadRefreshTokenAsync();
        Assert.True(stored.IsActive(DateTime.UtcNow));
        Assert.Null(stored.ReplacedByToken);
    }

    [Fact]
    public async Task Refresh_WithChangedRole_IssuesJwtWithCurrentRepositoryRole()
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        context.Roles = new List<string> { "Admin" };

        // Act
        var result = await context.Service.RefreshAsync(Request(token));

        // Assert
        Assert.True(result.Success);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value!.AccessToken);
        Assert.Equal("Admin", Assert.Single(jwt.Claims, claim => claim.Type == "role").Value);
        Assert.Equal("Admin", result.Value.User.Role);
        context.Users.Verify(users => users.GetRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_WhenGenerationFails_RollsBackConsumptionAndAnyInsertedReplacement(bool insertBeforeFailure)
    {
        // Arrange
        using var context = new AuthServiceTestContext();
        var token = await context.CreateRefreshTokenAsync();
        var failingTokens = new Mock<ITokenService>();
        failingTokens.Setup(tokens => tokens.HashRefreshToken(It.IsAny<string>()))
            .Returns((string raw) => context.Tokens.HashRefreshToken(raw));
        failingTokens.Setup(tokens => tokens.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (ApplicationUser user, IList<string> roles, CancellationToken ct) =>
            {
                if (insertBeforeFailure) await context.Tokens.CreateAsync(user, roles, ct);
                throw new InvalidOperationException("Simulated token generation failure");
            });

        // Act
        var result = await context.CreateService(failingTokens.Object).RefreshAsync(Request(token));

        // Assert
        Assert.Equal(AuthError.Unexpected, result.Error);
        Assert.Null(result.Value);
        var stored = await context.ReadRefreshTokenAsync();
        Assert.True(stored.IsActive(DateTime.UtcNow));
        Assert.Null(stored.ReplacedByToken);
        // A retry is a new request scope in the API; discard entities tracked by the rolled-back scope.
        context.Db.ChangeTracker.Clear();
        Assert.True((await context.Service.RefreshAsync(Request(token))).Success);
    }

    private static RefreshRequestDto Request(string token) => new() { RefreshToken = token };

    private static void AssertInvalid(ServiceResult<RefreshResponseDto> result)
    {
        Assert.False(result.Success);
        Assert.Equal(AuthError.InvalidRefreshToken, result.Error);
        Assert.Null(result.Value);
    }
}
