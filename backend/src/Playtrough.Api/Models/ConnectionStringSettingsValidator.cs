using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace Playtrough.Api.Models;

public sealed class ConnectionStringSettingsValidator : IValidateOptions<ConnectionStringSettings>
{
    public ValidateOptionsResult Validate(string? name, ConnectionStringSettings settings)
    {
        const string error = "ConnectionStrings:DefaultConnection must be a valid MySQL connection string with Server, Database and User ID; supply it via user-secrets or environment variables.";
        if (string.IsNullOrWhiteSpace(settings.DefaultConnection))
            return ValidateOptionsResult.Fail(error);
        try
        {
            var connection = new MySqlConnectionStringBuilder(settings.DefaultConnection);
            if (string.IsNullOrWhiteSpace(connection.Server) || string.IsNullOrWhiteSpace(connection.Database) ||
                string.IsNullOrWhiteSpace(connection.UserID) || connection.Port is 0 or > 65535)
                return ValidateOptionsResult.Fail(error);
            return ValidateOptionsResult.Success;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or MySqlException)
        {
            // Parser exceptions may contain the input, including credentials. Never forward them.
            return ValidateOptionsResult.Fail(error);
        }
    }
}
