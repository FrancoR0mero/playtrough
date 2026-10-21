using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class SmtpEmailSenderTests
{
    private static EmailSettings Settings() => new()
    {
        Host = "smtp.gmail.com", Port = 587, SecureSocketMode = EmailSecurity.StartTls,
        Username = "sender@example.com", Password = "test-only-password",
        FromAddress = "sender@example.com", FromName = "Playtrough", TimeoutSeconds = 15,
    };

    private static Mock<ISmtpClient> Client()
    {
        var client = new Mock<ISmtpClient>();
        client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        client.Setup(c => c.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        client.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>())).ReturnsAsync("accepted");
        client.Setup(c => c.DisconnectAsync(true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return client;
    }

    [Theory]
    [InlineData(EmailSecurity.None, SecureSocketOptions.None)]
    [InlineData(EmailSecurity.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(EmailSecurity.SslOnConnect, SecureSocketOptions.SslOnConnect)]
    public void MapSecurity_WithConfiguredMode_ReturnsExactMailKitMode(EmailSecurity mode, SecureSocketOptions expected)
    {
        // Act / Assert
        Assert.Equal(expected, SmtpEmailSender.MapSecurity(mode));
    }

    [Fact]
    public void MapSecurity_WithUnknownMode_RejectsInsteadOfDowngradingTls()
    {
        Assert.Throws<InvalidOperationException>(() => SmtpEmailSender.MapSecurity((EmailSecurity)999));
    }

    [Fact]
    public void CreateMessage_WithContent_PreservesSenderRecipientSubjectAndBothBodies()
    {
        // Arrange / Act
        using var message = SmtpEmailSender.CreateMessage(Settings(), "recipient@example.com", "Restablecé tu contraseña", "<p>Hola</p>", "Hola");

        // Assert
        var sender = Assert.Single(message.From.Mailboxes);
        Assert.Equal("Playtrough", sender.Name);
        Assert.Equal("sender@example.com", sender.Address);
        Assert.Equal("recipient@example.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Restablecé tu contraseña", message.Subject);
        Assert.Equal("<p>Hola</p>", message.HtmlBody);
        Assert.Equal("Hola", message.TextBody);
        Assert.IsType<MultipartAlternative>(message.Body);
    }

    [Fact]
    public async Task Send_WithCredentials_ConnectsAuthenticatesSendsAndDisposes()
    {
        // Arrange
        var client = Client();
        var settings = Settings();
        var sender = new SmtpEmailSender(Options.Create(settings), Mock.Of<ILogger<SmtpEmailSender>>(), () => client.Object);

        // Act
        await sender.SendAsync("recipient@example.com", "subject", "<p>body</p>", "body");

        // Assert
        client.VerifySet(c => c.Timeout = 15000, Times.Once);
        client.Verify(c => c.ConnectAsync(settings.Host, 587, SecureSocketOptions.StartTls, It.Is<CancellationToken>(ct => ct.CanBeCanceled)), Times.Once);
        client.Verify(c => c.AuthenticateAsync(settings.Username, settings.Password, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
        client.Verify(c => c.DisconnectAsync(true, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.Dispose(), Times.Once);
        var calls = client.Invocations.Select(call => call.Method.Name).ToList();
        Assert.True(calls.IndexOf("ConnectAsync") < calls.IndexOf("AuthenticateAsync"));
        Assert.True(calls.IndexOf("AuthenticateAsync") < calls.IndexOf("SendAsync"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_WithoutUsername_DoesNotAttemptAuthentication(string username)
    {
        // Arrange
        var settings = Settings();
        settings.Host = "localhost"; settings.Port = 1025; settings.SecureSocketMode = EmailSecurity.None;
        settings.Username = username; settings.Password = string.Empty;
        var client = Client();
        var sender = new SmtpEmailSender(Options.Create(settings), Mock.Of<ILogger<SmtpEmailSender>>(), () => client.Object);

        // Act
        await sender.SendAsync("recipient@example.com", "subject", "html", "text");

        // Assert: check every overload, not only username/password.
        Assert.DoesNotContain(client.Invocations, call => call.Method.Name == "AuthenticateAsync");
        client.Verify(c => c.ConnectAsync("localhost", 1025, SecureSocketOptions.None, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
    }

    [Fact]
    public async Task Send_WhenDisabled_DoesNotCreateTransportOrParseMessageAndLogsOnlyStatus()
    {
        // Arrange: no SMTP settings or even a valid recipient are needed when disabled.
        var logger = new Mock<ILogger<SmtpEmailSender>>();
        var created = false;
        var sender = new SmtpEmailSender(Options.Create(new EmailSettings { Enabled = false }), logger.Object, () =>
        { created = true; throw new InvalidOperationException("Transport must never be created."); });

        // Act
        await sender.SendAsync("invalid", "private-subject", "private-html", "private-text");

        // Assert
        Assert.False(created);
        var log = Assert.Single(logger.Invocations, call => call.Method.Name == "Log");
        Assert.Equal(LogLevel.Information, log.Arguments[0]);
        Assert.Contains("disabled", (log.Arguments[2]?.ToString() ?? string.Empty));
        Assert.DoesNotContain("private", (log.Arguments[2]?.ToString() ?? string.Empty));
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("authenticate")]
    [InlineData("send")]
    [InlineData("disconnect")]
    public async Task Send_WhenTransportFails_DisposesLogsSafelyAndPropagates(string stage)
    {
        // Arrange
        var client = Client();
        var logger = new Mock<ILogger<SmtpEmailSender>>();
        var failure = new IOException("test-only-password recipient@example.com <p>secret</p> token=private");
        switch (stage)
        {
            case "connect": client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure); break;
            case "authenticate": client.Setup(c => c.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure); break;
            case "send": client.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>())).ThrowsAsync(failure); break;
            case "disconnect": client.Setup(c => c.DisconnectAsync(true, It.IsAny<CancellationToken>())).ThrowsAsync(failure); break;
        }
        var sender = new SmtpEmailSender(Options.Create(Settings()), logger.Object, () => client.Object);

        // Act
        var thrown = await Assert.ThrowsAsync<IOException>(() => sender.SendAsync("recipient@example.com", "subject", "<p>secret</p>", "secret"));

        // Assert
        Assert.Same(failure, thrown);
        client.Verify(c => c.Dispose(), Times.Once);
        var log = Assert.Single(logger.Invocations, call => call.Method.Name == "Log");
        Assert.Equal(LogLevel.Error, log.Arguments[0]);
        var text = (log.Arguments[2]?.ToString() ?? string.Empty);
        Assert.Contains(nameof(IOException), text);
        Assert.Contains("SMTP connection failed", text);
        Assert.DoesNotContain("test-only-password", text);
        Assert.DoesNotContain("recipient@example.com", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("token=private", text);
        Assert.Null(log.Arguments[3]);
    }
}
