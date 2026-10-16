using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using Moq;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

/// <summary>Service tests use real Identity/token providers and isolated SQLite, without HTTP or SMTP.</summary>
internal sealed class PasswordRecoveryTestContext : IDisposable
{
    public LoginApiFactory Factory { get; } = new(runPasswordResetWorker: false);
    public Mock<ILogger<AuthService>> Logger => Factory.AuthLogger;

    public IServiceScope CreateScope() => Factory.Services.CreateScope();

    public async Task InitializeAsync()
    {
        await Factory.CreateUserAsync();
    }

    public async Task<AuthMessageDto> ForgotAsync(string email)
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = email });
    }

    public async Task ProcessAsync(string email)
    {
        using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuthService>().ProcessForgotPasswordAsync(email);
    }

    public async Task<RecoveryEmail> IssueEmailAsync()
    {
        await ProcessAsync(LoginApiFactory.Email);
        return await Factory.ReadEmailAsync();
    }

    public async Task<ServiceResult<AuthMessageDto>> ResetAsync(ResetPasswordRequestDto dto)
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthService>().ResetPasswordAsync(dto);
    }

    public async Task<ServiceResult<LoginResponseDto>> LoginAsync(string password = LoginApiFactory.Password)
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync(new LoginRequestDto
        {
            Identifier = LoginApiFactory.Username, Password = password,
        });
    }

    public async Task LockAsync()
    {
        using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(LoginApiFactory.Email))!;
        user.AccessFailedCount = 3;
        var result = await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));
        if (!result.Succeeded) throw new InvalidOperationException("Test account could not be locked.");
    }

    public void Dispose() => Factory.Dispose();
}
