using System.Text;
using Microsoft.Extensions.Options;

namespace Playtrough.Api.Models;

public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings settings)
    {
        var errors = new List<string>();
        var key = settings.Key ?? string.Empty;
        var normalized = string.Concat(key.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        string[] placeholders = ["placeholder", "changeme", "changethis", "replace", "reemplazar", "changeinproduction", "yoursecret", "yourjwt", "yourkey", "example", "ejemplo"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 ||
            normalized.Length == 0 || normalized.Distinct().Count() == 1 ||
            placeholders.Any(normalized.Contains))
            errors.Add("Jwt:Key must be a non-placeholder secret of at least 32 UTF-8 bytes, supplied via user-secrets or environment variables.");
        if (string.IsNullOrWhiteSpace(settings.Issuer)) errors.Add("Jwt:Issuer is required.");
        if (string.IsNullOrWhiteSpace(settings.Audience)) errors.Add("Jwt:Audience is required.");
        if (settings.AccessTokenExpirationMinutes is < 1 or > 60)
            errors.Add("Jwt:AccessTokenExpirationMinutes must be between 1 and 60.");
        if (settings.RefreshTokenExpirationDays is < 1 or > 30)
            errors.Add("Jwt:RefreshTokenExpirationDays must be between 1 and 30.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
