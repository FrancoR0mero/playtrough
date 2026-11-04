using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;

namespace Playtrough.Api.Data;

/// <summary>EF tooling must never execute application startup or the Identity seeder.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string connection;
        if (args.Contains("--offline", StringComparer.Ordinal))
        {
            // Used only for scaffolding / SQL generation. No connection is opened.
            connection = "Server=localhost;Database=playtrough_design;User ID=design_time;";
        }
        else
        {
            // Preserve the normal environment, user-secrets and CLI configuration
            // for explicit database commands, without building/running the application.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ApplicationName = typeof(AppDbContextFactory).Assembly.GetName().Name,
                ContentRootPath = Path.GetDirectoryName(typeof(AppDbContextFactory).Assembly.Location),
            });
            using var configuration = builder.Configuration;
            var settings = new ConnectionStringSettings
            {
                DefaultConnection = configuration.GetConnectionString("DefaultConnection") ?? string.Empty,
            };
            var validation = new ConnectionStringSettingsValidator().Validate(null, settings);
            if (validation.Failed)
                throw new OptionsValidationException(Options.DefaultName, typeof(ConnectionStringSettings), validation.Failures);
            connection = settings.DefaultConnection;
        }
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySQL(connection).Options);
    }
}
