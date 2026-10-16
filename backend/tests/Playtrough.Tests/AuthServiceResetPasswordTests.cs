using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class AuthServiceResetPasswordTests
{
    [Fact]
    public async Task ResetPassword_WithValidToken_ChangesPasswordAndNewLoginWorks()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var email = await context.IssueEmailAsync();

        // Act
        var result = await context.ResetAsync(email.Request());

        // Assert
        Assert.True(result.Success);
        Assert.True((await context.LoginAsync(RecoveryEmail.NewPassword)).Success);
        Assert.Equal(AuthError.InvalidCredentials, (await context.LoginAsync()).Error);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("tampered")]
    [InlineData("unknown-email")]
    [InlineData("wrong-account")]
    public async Task ResetPassword_WithInvalidTokenOrEmail_ReturnsSameGenericResult(string invalid)
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var email = await context.IssueEmailAsync();
        var request = email.Request();
        var originalStamp = (await context.Factory.ReadUserAsync()).SecurityStamp;
        switch (invalid)
        {
            case "invalid": request.Token = "%%%invalid%%%"; break;
            case "tampered":
                var chars = request.Token.ToCharArray();
                chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';
                request.Token = new string(chars);
                break;
            case "unknown-email": request.Email = "nobody@example.com"; break;
            case "wrong-account":
                using (var scope = context.CreateScope())
                {
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    Assert.True((await users.CreateAsync(new ApplicationUser
                    {
                        UserName = "other_reset", Email = "other_reset@example.com",
                    }, LoginApiFactory.Password)).Succeeded);
                }
                request.Email = "other_reset@example.com";
                break;
        }

        // Act
        var result = await context.ResetAsync(request);

        // Assert
        AssertInvalid(result);
        Assert.Equal(originalStamp, (await context.Factory.ReadUserAsync()).SecurityStamp);
        Assert.True((await context.LoginAsync()).Success);
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_ReturnsInvalidReset()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var email = await context.IssueEmailAsync();
        // Exercise the real provider's expiry check deterministically, without waiting an hour.
        context.Factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan = TimeSpan.FromMinutes(-1);

        // Act
        var result = await context.ResetAsync(email.Request());

        // Assert
        AssertInvalid(result);
        Assert.True((await context.LoginAsync()).Success);
    }

    [Fact]
    public async Task ResetPassword_WithReusedToken_FailsAfterSecurityStampChanges()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var email = await context.IssueEmailAsync();
        var originalStamp = (await context.Factory.ReadUserAsync()).SecurityStamp;
        Assert.True((await context.ResetAsync(email.Request())).Success);

        // Act
        var repeated = await context.ResetAsync(email.Request());

        // Assert
        AssertInvalid(repeated);
        Assert.NotEqual(originalStamp, (await context.Factory.ReadUserAsync()).SecurityStamp);
    }

    [Theory]
    [InlineData("x", "PasswordTooShort")]
    [InlineData("lowercase12345", "PasswordRequiresUpper")]
    [InlineData("UPPERCASE12345", "PasswordRequiresLower")]
    [InlineData("NoDigitsHere", "PasswordRequiresDigit")]
    public async Task ResetPassword_WithWeakPassword_ReportsFailedIdentityRule(string password, string code)
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var email = await context.IssueEmailAsync();

        // Act
        var result = await context.ResetAsync(email.Request(password));

        // Assert
        Assert.False(result.Success);
        Assert.Equal(AuthError.ValidationError, result.Error);
        Assert.Contains(result.IdentityErrors, error => error.Code == code && !string.IsNullOrWhiteSpace(error.Description));
        Assert.True((await context.LoginAsync()).Success);
    }

    [Fact]
    public async Task ResetPassword_OnSuccess_RevokesAllUserRefreshTokensOnly()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var first = await context.LoginAsync();
        var second = await context.LoginAsync();
        Assert.True(first.Success && second.Success);
        int otherUserId;
        using (var otherScope = context.CreateScope())
        {
            var users = otherScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var other = new ApplicationUser { UserName = "other_session", Email = "other_session@example.com" };
            Assert.True((await users.CreateAsync(other, LoginApiFactory.Password)).Succeeded);
            otherUserId = other.Id;
            await otherScope.ServiceProvider.GetRequiredService<ITokenService>().CreateAsync(other, ["User"]);
        }
        var email = await context.IssueEmailAsync();

        // Act
        Assert.True((await context.ResetAsync(email.Request())).Success);

        // Assert
        var stored = await context.Factory.ReadRefreshTokensAsync();
        Assert.Equal(3, stored.Count);
        Assert.All(stored.Where(token => token.UserId != otherUserId), token => Assert.NotNull(token.RevokedAt));
        Assert.True(Assert.Single(stored, token => token.UserId == otherUserId).IsActive(DateTime.UtcNow));
        using var scope = context.CreateScope();
        var refreshed = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .RefreshAsync(new RefreshRequestDto { RefreshToken = first.Value!.RefreshToken });
        Assert.Equal(AuthError.InvalidRefreshToken, refreshed.Error);
    }

    [Fact]
    public async Task ResetPassword_WithValidTokenForLockedAccount_ClearsLockoutAndFailedAttempts()
    {
        // Arrange: issue the link before lockout; forgot-password intentionally skips locked accounts.
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var email = await context.IssueEmailAsync();
        await context.LockAsync();
        Assert.Equal(3, (await context.Factory.ReadUserAsync()).AccessFailedCount);

        // Act
        var result = await context.ResetAsync(email.Request());

        // Assert
        Assert.True(result.Success);
        var user = await context.Factory.ReadUserAsync();
        Assert.Equal(0, user.AccessFailedCount);
        Assert.Null(user.LockoutEnd);
        Assert.True((await context.LoginAsync(RecoveryEmail.NewPassword)).Success);
    }

    private static void AssertInvalid(ServiceResult<AuthMessageDto> result)
    {
        Assert.False(result.Success);
        Assert.Equal(AuthError.InvalidPasswordReset, result.Error);
        Assert.Null(result.Value);
        Assert.Empty(result.IdentityErrors);
    }
}
