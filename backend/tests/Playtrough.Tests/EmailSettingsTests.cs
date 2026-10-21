using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class EmailSettingsTests
{
    private static EmailSettings Settings() => new()
    {
        Host = "localhost", Port = 1025, FromAddress = "sender@example.com", FromName = "Playtrough",
        SecureSocketMode = EmailSecurity.None,
    };

    private static ValidateOptionsResult Validate(EmailSettings settings, string environment = "Development")
    {
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(value => value.EnvironmentName).Returns(environment);
        return new EmailSettingsValidator(host.Object).Validate(null, settings);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Port")]
    [InlineData("FromAddress")]
    public void Startup_WithMissingRequiredSetting_FailsWithKeyAndNeverPassword(string key)
    {
        // Arrange: exercise the actual ValidateOnStart registration, with a mocked sender.
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<EmailSettings>(settings =>
            {
                settings.Password = "test-only-password-not-for-logs";
                if (key == "Host") settings.Host = "";
                if (key == "Port") settings.Port = 0;
                if (key == "FromAddress") settings.FromAddress = "";
            })));

        // Act / Assert
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains($"Email:{key}", error.Message);
        Assert.DoesNotContain("test-only-password-not-for-logs", error.Message);
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Fact]
    public void LocalMailHog_WithDefaults_IsValidWithoutCredentials()
    {
        Assert.True(Validate(Settings()).Succeeded);
    }

    [Theory]
    [InlineData("Port", -1)]
    [InlineData("Port", 65536)]
    [InlineData("TimeoutSeconds", 0)]
    [InlineData("TimeoutSeconds", 121)]
    public void Validate_WithOutOfRangeValue_ReturnsClearKey(string key, int value)
    {
        // Arrange
        var settings = Settings();
        if (key == "Port") settings.Port = value;
        else settings.TimeoutSeconds = value;

        // Act / Assert
        Assert.Contains($"Email:{key}", Validate(settings).FailureMessage);
    }

    [Fact]
    public void Validate_WithInvalidSecurityOrAddress_Fails()
    {
        var settings = Settings();
        settings.SecureSocketMode = (EmailSecurity)999;
        settings.FromAddress = "invalid";
        var result = Validate(settings);
        Assert.Contains("Email:SecureSocketMode", result.FailureMessage);
        Assert.Contains("Email:FromAddress", result.FailureMessage);
    }

    [Fact]
    public void Validate_WithUsernameButEmptyPassword_RejectsEmptyAuthentication()
    {
        var settings = Settings();
        settings.Username = "sender@example.com";
        Assert.Contains("Email:Password", Validate(settings, "Production").FailureMessage);
    }

    [Fact]
    public void Startup_WhenDisabled_AllowsMissingSmtpConfiguration()
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<EmailSettings>(settings =>
            {
                settings.Enabled = false; settings.Host = ""; settings.Port = 0; settings.FromAddress = "";
            })));
        using var client = configured.CreateClient();
        Assert.False(configured.Services.GetRequiredService<IOptions<EmailSettings>>().Value.Enabled);
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Fact]
    public void Startup_WithExplicitGmailInDevelopment_AcceptsConfigurationWithoutConnecting()
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.PostConfigure<EmailSettings>(settings =>
            {
                settings.Host = "smtp.gmail.com"; settings.Port = 587; settings.SecureSocketMode = EmailSecurity.StartTls;
                settings.Username = settings.FromAddress = "sender@example.com"; settings.Password = "test-only-password";
            }));
        });
        using var client = configured.CreateClient();
        Assert.Equal("smtp.gmail.com", configured.Services.GetRequiredService<IOptions<EmailSettings>>().Value.Host);
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Theory]
    [InlineData("sender")]
    [InlineData("credentials")]
    [InlineData("tls")]
    [InlineData("port")]
    public void Gmail_WithInvalidSetup_RejectsBeforeAnyConnection(string invalid)
    {
        var settings = Settings();
        settings.Host = "smtp.gmail.com"; settings.Port = 587; settings.SecureSocketMode = EmailSecurity.StartTls;
        settings.Username = settings.FromAddress; settings.Password = "test-only-password";
        if (invalid == "sender") settings.FromAddress = "other@example.com";
        if (invalid == "credentials") settings.Username = "";
        if (invalid == "tls") settings.SecureSocketMode = EmailSecurity.None;
        if (invalid == "port") settings.Port = 1025;
        var result = Validate(settings);
        Assert.True(result.Failed);
        Assert.Contains("Gmail", result.FailureMessage);
        Assert.DoesNotContain(settings.Password, result.FailureMessage);
    }
}
