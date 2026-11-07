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

public class AuthLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationFailure_LogsOperationAndTypeWithoutExceptionOrSensitiveData(bool register)
    {
        var repository = new Mock<IUserRepository>();
        var logger = new Mock<ILogger<AuthService>>();
        var transaction = new Mock<IDbContextTransaction>();
        var failure = new InvalidOperationException(
            "Password=test-password-canary; Authorization: Bearer test-access-canary; refresh=test-refresh-canary; " +
            "https://frontend.example/reset-password?token=test-reset-canary; " +
            "https://frontend.example/confirm-email?token=test-confirm-canary; " +
            "Server=localhost;Password=test-connection-canary;",
            new Exception("test-inner-canary"));
        repository.Setup(value => value.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        repository.Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        repository.Setup(value => value.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var service = new AuthService(repository.Object, logger.Object,
            IdentityTestFactory.SignInManager().Object, Mock.Of<ITokenService>(), Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IEmailSender>(), Mock.Of<IPasswordResetQueue>(), IdentityTestFactory.PasswordResetOptions(),
            Microsoft.Extensions.Options.Options.Create(new EmailConfirmationSettings()), Mock.Of<IEmailConfirmationQueue>(),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

        var error = register
            ? (await service.RegisterAsync(new RegisterRequestDto { Username = "test-private-user-canary", Email = "test-private@example.com", Password = "test-password-canary" })).Error
            : (await service.LoginAsync(new LoginRequestDto { Identifier = "test-private-user-canary", Password = "test-password-canary" })).Error;

        Assert.Equal(AuthError.Unexpected, error);
        var log = Assert.Single(logger.Invocations, call => call.Method.Name == "Log");
        Assert.Equal(LogLevel.Error, log.Arguments[0]);
        Assert.Null(log.Arguments[3]);
        var message = log.Arguments[2]?.ToString() ?? "";
        Assert.Contains(register ? "registration" : "login", message);
        Assert.Contains(nameof(InvalidOperationException), message);
        Assert.DoesNotContain("canary", message);
        Assert.DoesNotContain("test-private@example.com", message);
        Assert.DoesNotContain("https://", message);
        Assert.DoesNotContain("Authorization", message);
    }
}
