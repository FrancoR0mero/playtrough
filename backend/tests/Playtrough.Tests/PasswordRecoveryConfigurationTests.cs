using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class PasswordRecoveryConfigurationTests
{
    [Fact]
    public void Email_InDevelopment_RejectsRemoteSmtpBeforeSendingAnything()
    {
        // Arrange: LoginApiFactory still mocks IEmailSender, even for a rejected configuration.
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.PostConfigure<EmailSettings>(settings => settings.Host = "smtp.example.com"));
        });

        // Act / Assert
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("local MailHog", error.Message);
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Fact]
    public void PasswordReset_WithConfiguredLifetime_UsesSameMinutesInIdentityProvider()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<PasswordResetSettings>(settings => settings.TokenLifespanMinutes = 45)));

        // Act
        using var client = configured.CreateClient();
        var options = configured.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>>().Value;

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(45), options.TokenLifespan);
    }
}
