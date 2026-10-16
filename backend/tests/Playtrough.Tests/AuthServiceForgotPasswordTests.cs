using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class AuthServiceForgotPasswordTests
{
    [Fact]
    public async Task ForgotPassword_WithExistingEmail_SendsSpanishMultipartContentWithValidUrlSafeToken()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();

        // Act: the unit test explicitly processes the queued work with the real service.
        var result = await context.ForgotAsync(LoginApiFactory.Email);
        context.Factory.ResetQueue.Verify(queue => queue.Enqueue(LoginApiFactory.Email), Times.Once);
        await context.ProcessAsync(LoginApiFactory.Email);
        var email = await context.Factory.ReadEmailAsync();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(LoginApiFactory.Email, email.Recipient);
        Assert.Contains("Restablecé", email.Subject);
        Assert.Contains(LoginApiFactory.Username, email.HtmlBody);
        Assert.Contains(LoginApiFactory.Username, email.TextBody);
        Assert.Contains("60 minutos", email.TextBody);
        Assert.Contains("Si no lo pediste, ignorá este correo", email.HtmlBody);
        Assert.Contains("Si no lo pediste, ignorá este correo", email.TextBody);
        Assert.DoesNotContain("<img", email.HtmlBody);
        Assert.Equal("http://localhost:4200/reset-password", email.ResetLink.GetLeftPart(UriPartial.Path));
        var request = email.Request();
        Assert.Equal(LoginApiFactory.Email, request.Email);
        Assert.Matches("^[A-Za-z0-9_-]+$", request.Token);
        using var scope = context.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(request.Email))!;
        var raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        Assert.True(await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider, "ResetPassword", raw));
        Assert.Equal(TimeSpan.FromHours(1), scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("locked")]
    public async Task ForgotPassword_WithUnavailableAccount_DoesNotSendAndReturnsSameResult(string unavailable)
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var expected = await context.ForgotAsync(LoginApiFactory.Email);
        if (unavailable == "locked") await context.LockAsync();
        var address = unavailable == "unknown" ? "nobody@example.com" : LoginApiFactory.Email;

        // Act
        var result = await context.ForgotAsync(address);
        await context.ProcessAsync(address);

        // Assert
        Assert.Equal(expected, result);
        context.Factory.EmailSender.Verify(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WhenSenderFails_LogsSanitizedErrorWithoutExposingFailure()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        context.Factory.EmailSender.Setup(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SENSITIVE_TEST_SMTP_PASSWORD_AND_TOKEN"));
        var expected = await context.ForgotAsync("nobody@example.com");

        // Act
        var result = await context.ForgotAsync(LoginApiFactory.Email);
        var error = await Record.ExceptionAsync(() => context.ProcessAsync(LoginApiFactory.Email));

        // Assert
        Assert.Null(error);
        Assert.Equal(expected, result);
        var log = Assert.Single(context.Logger.Invocations, call => call.Method.Name == "Log" && call.Arguments[0] is LogLevel.Error);
        Assert.NotNull(log);
        var message = Assert.IsType<string>(log.Arguments[2]?.ToString());
        Assert.Contains("email processing failed", message);
        Assert.DoesNotContain("SENSITIVE_TEST", message);
        Assert.DoesNotContain(LoginApiFactory.Email, message);
        Assert.Null(log.Arguments[3]);
    }

    [Fact]
    public async Task ForgotPassword_WhileEmailIsPending_ReturnsWithoutWaitingForSending()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Factory.EmailSender.Setup(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        var processing = context.ProcessAsync(LoginApiFactory.Email);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            var request = context.ForgotAsync(LoginApiFactory.Email);

            // Assert
            Assert.True(request.IsCompletedSuccessfully);
            Assert.False(processing.IsCompleted);
            Assert.Equal(await context.ForgotAsync("nobody@example.com"), await request);
        }
        finally { release.TrySetResult(); await processing; }
    }

    [Fact]
    public async Task ForgotPassword_WithSpecialCharacters_EscapesUsernameInHtml()
    {
        // Arrange
        using var context = new PasswordRecoveryTestContext();
        await context.InitializeAsync();
        using (var scope = context.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.UserName, "Ana <gamer> & amigos"));

        // Act
        var email = await context.IssueEmailAsync();

        // Assert
        Assert.Contains("Ana &lt;gamer&gt; &amp; amigos", email.HtmlBody);
        Assert.DoesNotContain("Ana <gamer>", email.HtmlBody);
        Assert.Contains("Ana <gamer> & amigos", email.TextBody);
    }
}
