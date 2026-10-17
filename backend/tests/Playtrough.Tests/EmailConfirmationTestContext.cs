using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

internal sealed class EmailConfirmationTestContext : IDisposable
{
    public LoginApiFactory Factory { get; } = new(runPasswordResetWorker: false, runConfirmationWorker: false);

    public async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task<T> RunAsync<T>(Func<IAuthService, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAuthService>());
    }

    public async Task ProcessAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuthService>().ProcessResendConfirmationAsync(email);
    }

    public async Task SetConfirmedAsync(bool confirmed)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(LoginApiFactory.Email))!;
        user.EmailConfirmed = confirmed;
        if (!(await users.UpdateAsync(user)).Succeeded) throw new InvalidOperationException("Cannot update test account.");
    }

    public static RegisterRequestDto Registration() => new()
    {
        Username = LoginApiFactory.Username, Email = LoginApiFactory.Email, Password = LoginApiFactory.Password,
    };

    public static LoginRequestDto Login(string password = LoginApiFactory.Password) => new()
    {
        Identifier = LoginApiFactory.Email, Password = password,
    };

    public static ConfirmEmailRequestDto Confirmation(RecoveryEmail email)
    {
        var query = QueryHelpers.ParseQuery(email.ResetLink.Query);
        return new() { Email = query["email"].ToString(), Token = query["token"].ToString() };
    }

    public void Dispose() => Factory.Dispose();
}
