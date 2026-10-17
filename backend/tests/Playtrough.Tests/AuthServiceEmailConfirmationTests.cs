using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class AuthServiceEmailConfirmationTests
{
    [Fact]
    public async Task Register_WithValidRequest_SendsConfirmationAndCreatesUnconfirmedAccountWithoutSession()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();

        // Act
        var result = await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Value!.EmailConfirmationRequired);
        Assert.False((await context.Factory.ReadUserAsync()).EmailConfirmed);
        Assert.Empty(await context.Factory.ReadRefreshTokensAsync());
        var email = await context.Factory.ReadEmailAsync();
        Assert.Equal(LoginApiFactory.Email, email.Recipient);
        Assert.Equal("/confirm-email", email.ResetLink.AbsolutePath);
        var request = EmailConfirmationTestContext.Confirmation(email);
        Assert.Equal(LoginApiFactory.Email, request.Email);
        Assert.Matches("^[A-Za-z0-9_-]+$", request.Token);
        Assert.Contains(LoginApiFactory.Username, email.TextBody);
        Assert.Contains("1440", email.TextBody);
        Assert.Contains("Si no creaste esta cuenta", email.TextBody);
        Assert.Contains("Confirmar correo", email.HtmlBody);
    }

    [Fact]
    public async Task Register_WhenEmailDeliveryFails_PreservesUnconfirmedAccountAndLogsWithoutSensitiveData()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        context.Factory.EmailSender.Setup(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive-recipient-and-token"));

        // Act
        var result = await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));

        // Assert
        Assert.True(result.Success);
        Assert.False((await context.Factory.ReadUserAsync()).EmailConfirmed);
        Assert.Contains(context.Factory.AuthLogger.Invocations, call => call.Method.Name == "Log");
        Assert.All(context.Factory.AuthLogger.Invocations, call =>
        {
            Assert.DoesNotContain("sensitive-recipient-and-token", string.Join(" ", call.Arguments));
            Assert.DoesNotContain(LoginApiFactory.Email, string.Join(" ", call.Arguments));
        });
    }

    [Fact]
    public async Task ConfirmEmail_WithValidTokenTwice_ConfirmsAndReturnsIdenticalSuccess()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        var request = EmailConfirmationTestContext.Confirmation(await context.Factory.ReadEmailAsync());

        // Act
        var first = await context.RunAsync(service => service.ConfirmEmailAsync(request));
        var second = await context.RunAsync(service => service.ConfirmEmailAsync(request));

        // Assert
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Value, second.Value);
        Assert.True((await context.Factory.ReadUserAsync()).EmailConfirmed);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("malformed")]
    [InlineData("tampered")]
    [InlineData("unknown-email")]
    [InlineData("other-user")]
    public async Task ConfirmEmail_WithInvalidTokenOrAccount_ReturnsSameGenericError(string scenario)
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        var request = EmailConfirmationTestContext.Confirmation(await context.Factory.ReadEmailAsync());
        if (scenario == "invalid") request.Token = IdentityTokenEncoding.Encode("invalid");
        if (scenario == "malformed") request.Token = "%";
        if (scenario == "tampered") request.Token = IdentityTokenEncoding.Encode(IdentityTokenEncoding.Decode(request.Token) + "x");
        if (scenario == "unknown-email") request.Email = "missing@example.com";
        if (scenario == "other-user")
        {
            var registration = new RegisterRequestDto
            {
                Email = "other@example.com", Username = "other", Password = LoginApiFactory.Password,
            };
            await context.RunAsync(service => service.RegisterAsync(registration));
            request.Email = registration.Email;
        }

        // Act
        var result = await context.RunAsync(service => service.ConfirmEmailAsync(request));

        // Assert
        Assert.Equal(AuthError.InvalidEmailConfirmation, result.Error);
        Assert.Null(result.Value);
        Assert.False((await context.Factory.ReadUserAsync()).EmailConfirmed);
    }

    [Fact]
    public async Task Resend_WithUnknownConfirmedAndUnconfirmedAccounts_ReturnsSameMessageAndHonorsCooldown()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        context.Factory.EmailSender.Invocations.Clear();

        // Act
        var first = await context.RunAsync(service => service.ResendConfirmationAsync(new() { Email = LoginApiFactory.Email }));
        await context.ProcessAsync(LoginApiFactory.Email);
        var second = await context.RunAsync(service => service.ResendConfirmationAsync(new() { Email = LoginApiFactory.Email }));
        await context.ProcessAsync(LoginApiFactory.Email);
        var missing = await context.RunAsync(service => service.ResendConfirmationAsync(new() { Email = "missing@example.com" }));
        await context.ProcessAsync("missing@example.com");
        await context.SetConfirmedAsync(true);
        var confirmed = await context.RunAsync(service => service.ResendConfirmationAsync(new() { Email = LoginApiFactory.Email }));
        await context.ProcessAsync(LoginApiFactory.Email);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(first, missing);
        Assert.Equal(first, confirmed);
        context.Factory.EmailSender.Verify(sender => sender.SendAsync(LoginApiFactory.Email, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resend_AfterCooldownExpires_SendsAgain()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        context.Factory.EmailSender.Invocations.Clear();
        await context.ProcessAsync(LoginApiFactory.Email);
        context.Factory.CacheClock.UtcNow += TimeSpan.FromSeconds(61);

        // Act
        await context.ProcessAsync(LoginApiFactory.Email);

        // Assert
        context.Factory.EmailSender.Verify(sender => sender.SendAsync(LoginApiFactory.Email, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(true, AuthError.EmailNotConfirmed)]
    [InlineData(false, AuthError.InvalidCredentials)]
    public async Task Login_WithUnconfirmedEmail_ChecksPasswordBeforeRevealingState(bool correctPassword, AuthError expected)
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));

        // Act
        var result = await context.RunAsync(service => service.LoginAsync(EmailConfirmationTestContext.Login(
            correctPassword ? LoginApiFactory.Password : "wrong")));

        // Assert
        Assert.Equal(expected, result.Error);
        Assert.Null(result.Value);
        Assert.Empty(await context.Factory.ReadRefreshTokensAsync());
        Assert.Equal(correctPassword ? 0 : 1, (await context.Factory.ReadUserAsync()).AccessFailedCount);
    }

    [Fact]
    public async Task Refresh_AfterEmailMarkedUnconfirmed_ReturnsGenericUnauthorizedResult()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.Factory.CreateUserAsync();
        var login = await context.RunAsync(service => service.LoginAsync(EmailConfirmationTestContext.Login()));
        Assert.True(login.Success);
        await context.SetConfirmedAsync(false);

        // Act
        var result = await context.RunAsync(service => service.RefreshAsync(new() { RefreshToken = login.Value!.RefreshToken }));

        // Assert
        Assert.Equal(AuthError.InvalidRefreshToken, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ResetPassword_WithUnconfirmedAccount_ConfirmsEmailAndAllowsLogin()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        await context.Factory.ReadEmailAsync();
        using (var scope = context.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IAuthService>().ProcessForgotPasswordAsync(LoginApiFactory.Email);
        var email = await context.Factory.ReadEmailAsync();

        // Act
        var reset = await context.RunAsync(service => service.ResetPasswordAsync(email.Request()));
        var login = await context.RunAsync(service => service.LoginAsync(EmailConfirmationTestContext.Login(RecoveryEmail.NewPassword)));

        // Assert
        Assert.True(reset.Success);
        Assert.True((await context.Factory.ReadUserAsync()).EmailConfirmed);
        Assert.True(login.Success);
    }

    [Fact]
    public async Task TokenProviders_HaveIndependentConfiguredLifetimesAndRejectExpiredConfirmation()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        var request = EmailConfirmationTestContext.Confirmation(await context.Factory.ReadEmailAsync());
        var options = context.Factory.Services.GetRequiredService<IOptions<EmailConfirmationTokenProviderOptions>>().Value;
        Assert.Equal(TimeSpan.FromHours(24), options.TokenLifespan);
        Assert.Equal(TimeSpan.FromHours(1), context.Factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
        Assert.Equal(60, context.Factory.Services.GetRequiredService<IOptions<EmailConfirmationSettings>>().Value.ResendCooldownSeconds);
        options.TokenLifespan = TimeSpan.FromSeconds(-1);

        // Act
        var result = await context.RunAsync(service => service.ConfirmEmailAsync(request));

        // Assert
        Assert.Equal(AuthError.InvalidEmailConfirmation, result.Error);
        Assert.False((await context.Factory.ReadUserAsync()).EmailConfirmed);
    }
}
