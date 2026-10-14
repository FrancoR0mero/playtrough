using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
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

/// <summary>Unit tests for <see cref="AuthService"/> with a mocked repository.
/// No DB access: atomicity (rollback on failure) is asserted via the mocked transaction.</summary>
public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _repo = new();
    private readonly Mock<ILogger<AuthService>> _logger = new();
    private readonly Mock<IDbContextTransaction> _transaction = new();

    private AuthService CreateService() => new(_repo.Object, _logger.Object,
        IdentityTestFactory.SignInManager().Object, Mock.Of<ITokenService>());

    private static RegisterRequestDto ValidDto() => new()
    {
        Email = "test@example.com",
        Username = "testuser",
        Password = "Str0ngPass",
    };

    private void SetupNoDuplicates(string email, string username)
    {
        _repo.Setup(r => r.ExistsByEmailAsync(email, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.ExistsByUsernameAsync(username, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_transaction.Object);
    }

    [Fact]
    public async Task Register_Success_ReturnsOkAndCommit()
    {
        var dto = ValidDto();
        ApplicationUser? createdUser = null;
        SetupNoDuplicates(dto.Email, dto.Username);
        _repo.Setup(r => r.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password, It.IsAny<CancellationToken>()))
            .Callback<ApplicationUser, string, CancellationToken>((user, _, _) => createdUser = user)
            .ReturnsAsync(IdentityResult.Success);
        _repo.Setup(r => r.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService().RegisterAsync(dto);

        Assert.True(result.Success);
        Assert.Equal(AuthError.None, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal("testuser", result.Value.Username);
        Assert.Equal("test@example.com", result.Value.Email);
        Assert.NotNull(createdUser);
        Assert.Equal("testuser", createdUser.UserName);
        Assert.Equal("test@example.com", createdUser.Email);
        _repo.Verify(r => r.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User", It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_InputIsTrimmed_BeforeChecksAndCreate()
    {
        var dto = new RegisterRequestDto
        {
            Email = "  test@example.com  ",
            Username = "  testuser  ",
            Password = "Str0ngPass",
        };
        SetupNoDuplicates("test@example.com", "testuser");
        _repo.Setup(r => r.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        _repo.Setup(r => r.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);

        var result = await CreateService().RegisterAsync(dto);

        Assert.True(result.Success);
        Assert.Equal("testuser", result.Value!.Username);
        Assert.Equal("test@example.com", result.Value.Email);
        _repo.Verify(r => r.ExistsByEmailAsync("test@example.com", It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.ExistsByUsernameAsync("testuser", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_EmailDuplicated_ReturnsEmailAlreadyExists()
    {
        var dto = ValidDto();
        _repo.Setup(r => r.ExistsByEmailAsync(dto.Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateService().RegisterAsync(dto);

        Assert.False(result.Success);
        Assert.Equal(AuthError.EmailAlreadyExists, result.Error);
        Assert.Empty(result.IdentityErrors);
        _repo.Verify(r => r.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_UsernameDuplicated_ReturnsUsernameAlreadyExists()
    {
        var dto = ValidDto();
        _repo.Setup(r => r.ExistsByEmailAsync(dto.Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.ExistsByUsernameAsync(dto.Username, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateService().RegisterAsync(dto);

        Assert.False(result.Success);
        Assert.Equal(AuthError.UsernameAlreadyExists, result.Error);
        Assert.Empty(result.IdentityErrors);
        _repo.Verify(r => r.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsValidationErrorWithIdentityErrors()
    {
        var dto = ValidDto();
        var identityError = new IdentityError
        {
            Code = "PasswordTooShort",
            Description = "Passwords must be at least 8 characters.",
        };
        SetupNoDuplicates(dto.Email, dto.Username);
        _repo.Setup(r => r.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        var result = await CreateService().RegisterAsync(dto);

        Assert.False(result.Success);
        Assert.Equal(AuthError.ValidationError, result.Error);
        var error = Assert.Single(result.IdentityErrors);
        Assert.Equal("PasswordTooShort", error.Code);
        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_CreateRaceDuplicateEmail_MapsToEmailAlreadyExists()
    {
        var dto = ValidDto();
        SetupNoDuplicates(dto.Email, dto.Username);
        _repo.Setup(r => r.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateEmail",
                Description = "Email 'test@example.com' is already taken.",
            }));

        var result = await CreateService().RegisterAsync(dto);

        Assert.False(result.Success);
        Assert.Equal(AuthError.EmailAlreadyExists, result.Error);
        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_RoleAssignmentFails_ReturnsUnexpectedAndRollsBack()
    {
        var dto = ValidDto();
        SetupNoDuplicates(dto.Email, dto.Username);
        _repo.Setup(r => r.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success);
        _repo.Setup(r => r.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidRoleName", Description = "Role 'User' does not exist." }));

        var result = await CreateService().RegisterAsync(dto);

        Assert.False(result.Success);
        Assert.Equal(AuthError.Unexpected, result.Error);
        Assert.Single(result.IdentityErrors);
        _transaction.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
