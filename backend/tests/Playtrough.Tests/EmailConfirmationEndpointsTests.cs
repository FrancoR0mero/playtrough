using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Xunit;

namespace Playtrough.Tests;

public class EmailConfirmationEndpointsTests
{
    private static async Task InitializeAsync(LoginApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task Register_ConfirmTwice_Login_ReturnsCreatedThenSuccessAndRejectsInvalidToken()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);

        // Act
        using var registration = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());
        var email = await factory.ReadEmailAsync();
        var request = EmailConfirmationTestContext.Confirmation(email);
        using var first = await client.PostAsJsonAsync("/api/auth/confirm-email", request);
        using var second = await client.PostAsJsonAsync("/api/auth/confirm-email", request);
        using var login = await client.PostAsJsonAsync("/api/auth/login", EmailConfirmationTestContext.Login());
        request.Email = "missing@example.com";
        using var invalid = await client.PostAsJsonAsync("/api/auth/confirm-email", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.True((await registration.Content.ReadFromJsonAsync<RegisterResponseDto>())!.EmailConfirmationRequired);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectPasswordAndUnconfirmedEmail_ReturnsForbiddenWithCode()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        using var registration = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());

        // Act
        using var correct = await client.PostAsJsonAsync("/api/auth/login", EmailConfirmationTestContext.Login());
        using var wrong = await client.PostAsJsonAsync("/api/auth/login", EmailConfirmationTestContext.Login("wrong"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, correct.StatusCode);
        var problem = await correct.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("email_not_confirmed", problem!.Extensions["code"]!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Empty(await factory.ReadRefreshTokensAsync());
        Assert.Equal(1, (await factory.ReadUserAsync()).AccessFailedCount);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("other-user")]
    public async Task ConfirmEmail_WithInvalidTokenOrWrongAccount_ReturnsSame400Problem(string scenario)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        using var registration = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());
        var request = EmailConfirmationTestContext.Confirmation(await factory.ReadEmailAsync());
        if (scenario == "invalid") request.Token += "tampered";
        if (scenario == "missing") request.Email = "missing@example.com";
        if (scenario == "other-user")
        {
            var other = new RegisterRequestDto
            {
                Username = "other", Email = "other@example.com", Password = LoginApiFactory.Password,
            };
            using var otherRegistration = await client.PostAsJsonAsync("/api/auth/register", other);
            Assert.Equal(HttpStatusCode.Created, otherRegistration.StatusCode);
            request.Email = other.Email;
        }

        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/confirm-email", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("The email confirmation link is invalid or has expired.", problem!.Detail);
        Assert.False((await factory.ReadUserAsync()).EmailConfirmed);
    }

    [Fact]
    public async Task ResendConfirmation_WithAllAccountStates_ReturnsIdentical200Body()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        using var registration = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());
        var confirmation = EmailConfirmationTestContext.Confirmation(await factory.ReadEmailAsync());

        // Act
        using var pending = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = LoginApiFactory.Email });
        await factory.ReadEmailAsync();
        using var repeat = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = LoginApiFactory.Email });
        using var missing = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = "missing@example.com" });
        using var confirm = await client.PostAsJsonAsync("/api/auth/confirm-email", confirmation);
        using var confirmed = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = LoginApiFactory.Email });

        // Assert
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        foreach (var response in new[] { pending, repeat, missing, confirmed })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(await pending.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("/api/auth/resend-confirmation", "{}")]
    [InlineData("/api/auth/resend-confirmation", "{\"email\":\"invalid\"}")]
    [InlineData("/api/auth/resend-confirmation", "{\"email\":\" \"}")]
    [InlineData("/api/auth/resend-confirmation", "null")]
    [InlineData("/api/auth/resend-confirmation", "")]
    [InlineData("/api/auth/confirm-email", "{}")]
    [InlineData("/api/auth/confirm-email", "{\"email\":\"ana@example.com\"}")]
    [InlineData("/api/auth/confirm-email", "null")]
    public async Task ConfirmationEndpoints_WithInvalidBody_Return400Problem(string route, string json)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(route, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ResendConfirmation_WhileDeliveryIsPending_DoesNotWaitForSmtp()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        using var registration = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.EmailSender.Setup(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        try
        {
            // Act
            using var existing = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = LoginApiFactory.Email })
                .WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var missing = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = "missing@example.com" })
                .WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.False(release.Task.IsCompleted);
            Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
            Assert.Equal(await existing.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Register_ForgotPassword_Reset_Login_ConfirmsAccount()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        using var registration = await client.PostAsJsonAsync("/api/auth/register", EmailConfirmationTestContext.Registration());
        await factory.ReadEmailAsync();

        // Act
        using var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = LoginApiFactory.Email });
        var resetEmail = await factory.ReadEmailAsync();
        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password", resetEmail.Request());
        using var login = await client.PostAsJsonAsync("/api/auth/login", EmailConfirmationTestContext.Login(RecoveryEmail.NewPassword));

        // Assert
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.True((await factory.ReadUserAsync()).EmailConfirmed);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
