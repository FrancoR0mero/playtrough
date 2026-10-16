using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class AuthServiceLoginTests
{
    private readonly Mock<IUserRepository> _repo = new();
    private readonly Mock<SignInManager<ApplicationUser>> _signIn = IdentityTestFactory.SignInManager();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly ApplicationUser _user = new() { Id = 42, UserName = "ana", Email = "ana@example.com" };

    private AuthService Service() => new(_repo.Object, Mock.Of<ILogger<AuthService>>(), _signIn.Object, _tokens.Object,
        Mock.Of<IRefreshTokenRepository>(), Mock.Of<IEmailSender>(), Mock.Of<IPasswordResetQueue>(),
        IdentityTestFactory.PasswordResetOptions());

    private void SetupUser(bool success = true)
    {
        _repo.Setup(r => r.FindByEmailAsync(_user.Email!, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _repo.Setup(r => r.FindByUsernameAsync(_user.UserName!, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, "password", true))
            .ReturnsAsync(success ? SignInResult.Success : SignInResult.Failed);
        _repo.Setup(r => r.ResetAccessFailedCountAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync(IdentityResult.Success);
        _repo.Setup(r => r.GetRolesAsync(_user, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "User", "Admin" });
        _tokens.Setup(t => t.CreateAsync(_user, It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenPair("access", "refresh", DateTime.UtcNow.AddMinutes(15)));
    }

    [Theory]
    [InlineData("  ana@example.com  ", true)]
    [InlineData("  ana  ", false)]
    public async Task Login_ResolvesIdentifierResetsFailuresAndReturnsTokens(string identifier, bool email)
    {
        SetupUser();
        var result = await Service().LoginAsync(new() { Identifier = identifier, Password = "password" });
        Assert.True(result.Success);
        Assert.Equal("access", result.Value!.AccessToken);
        Assert.Equal("refresh", result.Value.RefreshToken);
        Assert.Equal(new LoginUserDto(42, "ana", "ana@example.com", "Admin"), result.Value.User);
        _repo.Verify(r => r.FindByEmailAsync(_user.Email!, It.IsAny<CancellationToken>()), email ? Times.Once() : Times.Never());
        _repo.Verify(r => r.FindByUsernameAsync(_user.UserName!, It.IsAny<CancellationToken>()), email ? Times.Never() : Times.Once());
        _signIn.Verify(s => s.CheckPasswordSignInAsync(_user, "password", true), Times.Once);
        _repo.Verify(r => r.ResetAccessFailedCountAsync(_user, It.IsAny<CancellationToken>()), Times.Once);
        _tokens.Verify(t => t.CreateAsync(_user, It.Is<IList<string>>(r => r.Contains("User") && r.Contains("Admin")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_UnknownUserAndWrongPasswordHaveSameErrorAndIssueNoTokens(bool userExists)
    {
        if (userExists) SetupUser(success: false);
        var result = await Service().LoginAsync(new() { Identifier = "ana", Password = "password" });
        Assert.False(result.Success);
        Assert.Equal(AuthError.InvalidCredentials, result.Error);
        Assert.Null(result.Value);
        _tokens.Verify(t => t.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        _signIn.Verify(s => s.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), true), userExists ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Login_LockedAccountReturnsLockedAndIssuesNoTokens()
    {
        SetupUser();
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, "password", true)).ReturnsAsync(SignInResult.LockedOut);
        var result = await Service().LoginAsync(new() { Identifier = "ana", Password = "password" });
        Assert.Equal(AuthError.AccountLocked, result.Error);
        _tokens.Verify(t => t.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_ResetFailureDoesNotIssueTokens()
    {
        SetupUser();
        _repo.Setup(r => r.ResetAccessFailedCountAsync(_user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));
        var result = await Service().LoginAsync(new() { Identifier = "ana", Password = "password" });
        Assert.Equal(AuthError.Unexpected, result.Error);
        _tokens.Verify(t => t.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_PersistenceFailureReturnsUnexpectedWithoutExposingTokens()
    {
        SetupUser();
        _tokens.Setup(t => t.CreateAsync(_user, It.IsAny<IList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Internal DB failure"));
        var result = await Service().LoginAsync(new() { Identifier = "ana", Password = "password" });
        Assert.Equal(AuthError.Unexpected, result.Error);
        Assert.Null(result.Value);
    }
}
