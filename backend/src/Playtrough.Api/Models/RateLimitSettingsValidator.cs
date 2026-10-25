using Microsoft.Extensions.Options;

namespace Playtrough.Api.Models;

public sealed class RateLimitSettingsValidator : IValidateOptions<RateLimitSettings>
{
    public ValidateOptionsResult Validate(string? name, RateLimitSettings settings)
    {
        var errors = new List<string>();
        foreach (var (key, policy) in new[]
        {
            (nameof(settings.Login), settings.Login), (nameof(settings.Register), settings.Register),
            (nameof(settings.PasswordRecovery), settings.PasswordRecovery), (nameof(settings.TokenActions), settings.TokenActions),
            (nameof(settings.Refresh), settings.Refresh), (nameof(settings.Global), settings.Global),
            (nameof(settings.Catalog), settings.Catalog),
        })
        {
            if (policy is null)
            {
                errors.Add($"RateLimit:{key} is required.");
                continue;
            }
            if (policy.PermitLimit <= 0) errors.Add($"RateLimit:{key}:PermitLimit must be positive.");
            if (policy.Window <= TimeSpan.Zero) errors.Add($"RateLimit:{key}:Window must be a positive duration.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
