using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Dtos.Auth;
using Xunit;

namespace Playtrough.Tests;

public class PasswordRecoveryEndpointsTests
{
    private const string InvalidDetail = "The password reset link is invalid or has expired.";

    [Fact]
    public async Task PasswordRecovery_FullFlow_ChangesPasswordAndInvalidatesOldLoginAndRefresh()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        using var login = await LoginAsync(client, LoginApiFactory.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var original = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(original);
        var email = await IssueEmailAsync(factory, client);

        // Act
        using var reset = await client.PostAsJsonAsync("/api/auth/reset-password", email.Request());
        using var newLogin = await LoginAsync(client, RecoveryEmail.NewPassword);
        using var oldLogin = await LoginAsync(client, LoginApiFactory.Password);
        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequestDto { RefreshToken = original.RefreshToken });

        // Assert
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await reset.Content.ReadFromJsonAsync<AuthMessageDto>())?.Message));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        var tokens = await factory.ReadRefreshTokensAsync();
        Assert.Contains(tokens, token => token.RevokedAt is not null);
    }

    [Fact]
    public async Task ForgotPassword_WithExistingOrMissingEmail_ReturnsIdentical200Body()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();

        // Act
        using var existing = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = LoginApiFactory.Email });
        using var missing = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Equal(await existing.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        await factory.ReadEmailAsync();
        // A subsequent known request is a FIFO marker: when its email arrives, the missing job has finished.
        using var marker = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = LoginApiFactory.Email });
        Assert.Equal(HttpStatusCode.OK, marker.StatusCode);
        await factory.ReadEmailAsync();
        factory.EmailSender.Verify(sender => sender.SendAsync("nobody@example.com", It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WhileSmtpIsPending_DoesNotWaitForSendingOrRevealAccountExistence()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.EmailSender.Setup(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        try
        {
            // Act
            using var existing = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = LoginApiFactory.Email })
                .WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var missing = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@example.com" })
                .WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.False(release.Task.IsCompleted);
            Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
            Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
            Assert.Equal(await existing.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("empty-body")]
    [InlineData("null-json")]
    [InlineData("missing-email")]
    [InlineData("empty-email")]
    [InlineData("invalid-email")]
    [InlineData("whitespace")]
    public async Task ForgotPassword_WithInvalidBody_Returns400(string invalid)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();

        // Act
        using var response = invalid switch
        {
            "empty-body" => await client.PostAsync("/api/auth/forgot-password", null),
            "null-json" => await client.PostAsJsonAsync<object?>("/api/auth/forgot-password", null),
            "missing-email" => await client.PostAsJsonAsync("/api/auth/forgot-password", new { }),
            "empty-email" => await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "" }),
            "whitespace" => await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "   " }),
            _ => await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "invalid-email" }),
        };

        // Assert
        await AssertValidationAsync(response);
        factory.EmailSender.Verify(sender => sender.SendAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("invalid-token")]
    [InlineData("unknown-email")]
    [InlineData("expired-token")]
    public async Task ResetPassword_WithInvalidLink_ReturnsSame400Problem(string invalid)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var request = (await IssueEmailAsync(factory, client)).Request();
        if (invalid == "invalid-token") request.Token = "%%%invalid%%%";
        else if (invalid == "unknown-email") request.Email = "nobody@example.com";
        else factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan = TimeSpan.FromMinutes(-1);

        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/reset-password", request);

        // Assert
        await AssertInvalidLinkAsync(response);
    }

    [Fact]
    public async Task ResetPassword_WithReusedToken_ReturnsBadRequest()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var request = (await IssueEmailAsync(factory, client)).Request();
        using var first = await client.PostAsJsonAsync("/api/auth/reset-password", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using var second = await client.PostAsJsonAsync("/api/auth/reset-password", request);

        // Assert
        await AssertInvalidLinkAsync(second);
    }

    [Fact]
    public async Task ResetPassword_WithWeakPassword_ReturnsFailedRuleInValidationProblem()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var email = await IssueEmailAsync(factory, client);

        // Act
        using var response = await client.PostAsJsonAsync("/api/auth/reset-password", email.Request("x"));

        // Assert
        var problem = await AssertValidationAsync(response);
        Assert.Contains(nameof(ResetPasswordRequestDto.NewPassword), problem.Errors.Keys);
        Assert.Contains(problem.Errors[nameof(ResetPasswordRequestDto.NewPassword)], message => message.Contains("at least 8", StringComparison.Ordinal));
        using var corrected = await client.PostAsJsonAsync("/api/auth/reset-password", email.Request());
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
    }

    [Theory]
    [InlineData("empty-body")]
    [InlineData("null-json")]
    [InlineData("missing-fields")]
    [InlineData("mismatch")]
    [InlineData("invalid-email")]
    [InlineData("empty-token")]
    [InlineData("missing-password")]
    public async Task ResetPassword_WithInvalidBody_Returns400WithoutConsumingLink(string invalid)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        await factory.CreateUserAsync();
        var email = await IssueEmailAsync(factory, client);
        var dto = email.Request();
        if (invalid == "mismatch") dto.ConfirmPassword = "different";
        if (invalid == "invalid-email") dto.Email = "not-an-email";
        if (invalid == "empty-token") dto.Token = "";
        if (invalid == "missing-password") dto.NewPassword = dto.ConfirmPassword = "";

        // Act
        using var response = invalid switch
        {
            "empty-body" => await client.PostAsync("/api/auth/reset-password", null),
            "null-json" => await client.PostAsJsonAsync<object?>("/api/auth/reset-password", null),
            "missing-fields" => await client.PostAsJsonAsync("/api/auth/reset-password", new { }),
            _ => await client.PostAsJsonAsync("/api/auth/reset-password", dto),
        };

        // Assert
        await AssertValidationAsync(response);
        using var corrected = await client.PostAsJsonAsync("/api/auth/reset-password", email.Request());
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
        => client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto { Identifier = LoginApiFactory.Username, Password = password });

    private static async Task<RecoveryEmail> IssueEmailAsync(LoginApiFactory factory, HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = LoginApiFactory.Email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await factory.ReadEmailAsync();
    }

    private static async Task<ValidationProblemDetails> AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
        return problem;
    }

    private static async Task AssertInvalidLinkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid password reset request", problem.Title);
        Assert.Equal(InvalidDetail, problem.Detail);
    }
}
