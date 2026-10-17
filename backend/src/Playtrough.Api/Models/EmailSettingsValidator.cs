using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Playtrough.Api.Models;

public sealed class EmailSettingsValidator(IHostEnvironment environment) : IValidateOptions<EmailSettings>
{
    public ValidateOptionsResult Validate(string? name, EmailSettings settings)
    {
        // Disabled deployments do not require SMTP settings or credentials.
        if (!settings.Enabled) return ValidateOptionsResult.Success;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.Host)) errors.Add("Email:Host is required.");
        if (settings.Port is < 1 or > 65535) errors.Add("Email:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(settings.FromAddress) || !new EmailAddressAttribute().IsValid(settings.FromAddress))
            errors.Add("Email:FromAddress must be a valid email address.");
        if (!Enum.IsDefined(settings.SecureSocketMode)) errors.Add("Email:SecureSocketMode is invalid.");
        if (settings.TimeoutSeconds is < 1 or > 120) errors.Add("Email:TimeoutSeconds must be between 1 and 120.");
        if (!string.IsNullOrWhiteSpace(settings.Username) && string.IsNullOrWhiteSpace(settings.Password))
            errors.Add("Email:Password is required when Email:Username is configured.");

        var gmail = string.Equals(settings.Host, "smtp.gmail.com", StringComparison.OrdinalIgnoreCase);
        if (gmail)
        {
            if (settings.Port != 587 || settings.SecureSocketMode != EmailSecurity.StartTls)
                errors.Add("Gmail requires Email:Port=587 and Email:SecureSocketMode=StartTls.");
            if (string.IsNullOrWhiteSpace(settings.Username) ||
                !new EmailAddressAttribute().IsValid(settings.Username) ||
                !string.Equals(settings.FromAddress, settings.Username, StringComparison.OrdinalIgnoreCase))
                errors.Add("Gmail requires Email:Username and a matching Email:FromAddress.");
        }

        var localMailHog = (settings.Host is "localhost" or "127.0.0.1" or "::1") &&
            settings.Port == 1025 && settings.SecureSocketMode == EmailSecurity.None &&
            string.IsNullOrEmpty(settings.Username) && string.IsNullOrEmpty(settings.Password);
        if (environment.IsDevelopment() && !localMailHog && !gmail)
            errors.Add("Development email must use local MailHog on port 1025 without credentials or TLS, or explicitly configured Gmail.");
        // Error text names keys and requirements only; never interpolate configuration values.
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
