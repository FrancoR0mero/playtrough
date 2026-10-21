using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Authorization;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

internal sealed class AdminTestContext(bool concurrent = false) : IDisposable
{
    public LoginApiFactory Factory { get; } = new(useIndependentConnections: concurrent);
    public int ActorId { get; private set; }
    public int TargetId { get; private set; }
    public int OtherId { get; private set; }

    public async Task InitializeAsync()
    {
        await Factory.CreateUserAsync();
        TargetId = (await Factory.ReadUserAsync()).Id;
        ActorId = await CreateUserAsync("actor_admin", "actor_admin@example.com", AppRoles.Admin);
        OtherId = await CreateUserAsync("other_user", "different_address@example.net", AppRoles.User, false);
    }

    public async Task<int> CreateUserAsync(string username, string email, string role, bool confirmed = true)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = username, Email = email, EmailConfirmed = confirmed };
        Assert.True((await users.CreateAsync(user, LoginApiFactory.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    public async Task<LoginResponseDto> LoginAsync(HttpClient client, string username = "actor_admin")
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = username, password = LoginApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    }

    public async Task<HttpClient> AdminClientAsync()
    {
        var client = Factory.CreateClient();
        var login = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    public void Dispose() => Factory.Dispose();
}
