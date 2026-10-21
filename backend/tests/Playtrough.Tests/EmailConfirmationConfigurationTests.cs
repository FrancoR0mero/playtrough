using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Migrations;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class EmailConfirmationConfigurationTests
{
    [Fact]
    public void Confirmation_WithCustomLifetime_DoesNotChangePasswordResetLifetime()
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<EmailConfirmationSettings>(settings => settings.TokenLifespanMinutes = 120)));

        // Act
        using var client = configured.CreateClient();
        var confirmation = configured.Services.GetRequiredService<IOptions<EmailConfirmationTokenProviderOptions>>().Value;
        var reset = configured.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>>().Value;

        // Assert
        Assert.Equal(TimeSpan.FromHours(2), confirmation.TokenLifespan);
        Assert.Equal(TimeSpan.FromHours(1), reset.TokenLifespan);
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1440, 0)]
    public void Confirmation_WithInvalidDurations_RejectsConfiguration(int lifetime, int cooldown)
    {
        // Arrange
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<EmailConfirmationSettings>(settings =>
            {
                settings.TokenLifespanMinutes = lifetime;
                settings.ResendCooldownSeconds = cooldown;
            })));

        // Act / Assert
        Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Empty(factory.EmailSender.Invocations);
    }

    [Fact]
    public async Task DataMigration_ConfirmsExistingAccountsAndLeavesLaterRegistrationsUnconfirmed()
    {
        // Arrange
        using var context = new EmailConfirmationTestContext();
        await context.InitializeAsync();
        await context.RunAsync(service => service.RegisterAsync(EmailConfirmationTestContext.Registration()));
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migration = new ConfirmExistingUserEmails();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations)).Sql;

        // Act: only this test database is updated; no real database or migration history is touched.
        await db.Database.ExecuteSqlRawAsync(sql);
        var later = new ApplicationUser { Email = "later@example.com", UserName = "later" };
        db.Users.Add(later);
        await db.SaveChangesAsync();

        // Assert
        Assert.True((await context.Factory.ReadUserAsync()).EmailConfirmed);
        Assert.False((await db.Users.AsNoTracking().SingleAsync(user => user.Id == later.Id)).EmailConfirmed);
        Assert.Empty(migration.DownOperations);
        Assert.Contains("20261017000000_ConfirmExistingUserEmails", db.GetService<IMigrationsAssembly>().Migrations.Keys);
    }
}
