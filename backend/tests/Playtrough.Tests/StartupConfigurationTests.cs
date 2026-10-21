using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public class StartupConfigurationTests
{
    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("Jwt:Key")]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    [InlineData("Jwt:AccessTokenExpirationMinutes")]
    [InlineData("Jwt:RefreshTokenExpirationDays")]
    [InlineData("PasswordReset:FrontendBaseUrl")]
    [InlineData("Email:Host")]
    [InlineData("Email:Port")]
    [InlineData("Email:FromAddress")]
    [InlineData("Email:Password")]
    public void Production_WithMissingRequiredSetting_FailsWithKeyAndNoSecrets(string key)
    {
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<ConnectionStringSettings>(settings =>
                settings.DefaultConnection = key == "ConnectionStrings:DefaultConnection" ? "" :
                    "Server=localhost;Database=tests;User ID=test_user;Password=test-only-database-canary;");
            services.PostConfigure<JwtSettings>(settings =>
            {
                if (key == "Jwt:Key") settings.Key = "";
                if (key == "Jwt:Issuer") settings.Issuer = "";
                if (key == "Jwt:Audience") settings.Audience = "";
                if (key == "Jwt:AccessTokenExpirationMinutes") settings.AccessTokenExpirationMinutes = 0;
                if (key == "Jwt:RefreshTokenExpirationDays") settings.RefreshTokenExpirationDays = 0;
            });
            services.PostConfigure<PasswordResetSettings>(settings =>
            {
                if (key == "PasswordReset:FrontendBaseUrl") settings.FrontendBaseUrl = "";
            });
            services.PostConfigure<EmailSettings>(settings =>
            {
                settings.Enabled = true;
                settings.Username = "sender@example.com";
                settings.Password = "test-only-smtp-canary";
                if (key == "Email:Host") settings.Host = "";
                if (key == "Email:Port") settings.Port = 0;
                if (key == "Email:FromAddress") settings.FromAddress = "";
                if (key == "Email:Password") settings.Password = "";
            });
        }));

        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains(key, error.Message);
        Assert.DoesNotContain(factory.SigningKey, error.ToString());
        Assert.DoesNotContain("test-only-database-canary", error.ToString());
        Assert.DoesNotContain("test-only-smtp-canary", error.ToString());
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Theory]
    [InlineData("short-test-key")]
    [InlineData("                                ")]
    [InlineData("CHANGE-ME-CHANGE-ME-CHANGE-ME-CHANGE-ME")]
    [InlineData("PLACEHOLDER_012345678901234567890123456789")]
    [InlineData("your_secret_key_012345678901234567890123")]
    [InlineData("REEMPLAZAR_POR_CLAVE_ALEATORIA_PRIVADA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Startup_WithShortOrPlaceholderJwtKey_RejectsWithoutEchoingIt(string key)
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            using var factory = new LoginApiFactory(environment: environment);
            using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtSettings>(settings => settings.Key = key)));
            var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
            Assert.Contains("Jwt:Key", error.Message);
            Assert.DoesNotContain(key, error.ToString());
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Server=localhost;User ID=test_user;Password=test-only-db-canary;")]
    [InlineData("Server=;Database=tests;User ID=test_user;Password=test-only-db-canary;")]
    [InlineData("Database=tests;User ID=test_user;Password=test-only-db-canary;")]
    [InlineData("Server=localhost;Database=tests;Password=test-only-db-canary;")]
    [InlineData("Server=localhost;Database=tests;User ID=test_user;Port=0;Password=test-only-db-canary;")]
    [InlineData("Server=localhost;Database=tests;User ID=test_user;Port=65536;Password=test-only-db-canary;")]
    [InlineData("Server=localhost;Port=test-only-db-canary;")]
    [InlineData("test-only-db-canary")]
    [InlineData("Server=localhost;Unknown=test-only-db-canary;")]
    public void Startup_WithInvalidConnectionString_FailsWithoutParserDetails(string connection)
    {
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<ConnectionStringSettings>(settings => settings.DefaultConnection = connection)));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("ConnectionStrings:DefaultConnection", error.Message);
        Assert.DoesNotContain("test-only-db-canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("relative/test-only-url-canary")]
    [InlineData("ftp://frontend.example/test-only-url-canary")]
    [InlineData("https://user:test-only-url-canary@frontend.example")]
    [InlineData("https://frontend.example?token=test-only-url-canary")]
    [InlineData("https://frontend.example#test-only-url-canary")]
    public void Startup_WithInvalidFrontendUrl_FailsWithoutEchoingIt(string url)
    {
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<PasswordResetSettings>(settings => settings.FrontendBaseUrl = url)));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("PasswordReset:FrontendBaseUrl", error.Message);
        Assert.DoesNotContain("test-only-url-canary", error.ToString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Startup_WithValidConfiguration_StartsWithoutExternalCredentials(string environment)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("Jwt:AccessTokenExpirationMinutes")]
    [InlineData("Jwt:RefreshTokenExpirationDays")]
    [InlineData("Email:Enabled")]
    [InlineData("Email:Port")]
    [InlineData("Email:SecureSocketMode")]
    [InlineData("Email:TimeoutSeconds")]
    [InlineData("PasswordReset:TokenLifespanMinutes")]
    [InlineData("EmailConfirmation:TokenLifespanMinutes")]
    public void Startup_WithInvalidValueType_DoesNotExposeBinderInnerException(string key)
    {
        using var factory = new LoginApiFactory(environment: "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = "test-only-binding-canary" })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains(key.Split(':')[0], error.Message);
        Assert.DoesNotContain("test-only-binding-canary", error.ToString());
        Assert.Null(error.InnerException);
    }
}
