using Microsoft.Extensions.Options;

namespace Playtrough.Api.Models;

public sealed class RawgSettingsValidator : IValidateOptions<RawgSettings>
{
    public ValidateOptionsResult Validate(string? name, RawgSettings settings)
    {
        var errors = new List<string>();
        if (settings.Enabled && string.IsNullOrWhiteSpace(settings.ApiKey))
            errors.Add("RawgSettings:ApiKey is required when RawgSettings:Enabled is true.");
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            errors.Add("RawgSettings:BaseUrl must be an absolute HTTPS URL without credentials, query or fragment.");
        if (settings.MaxRequestsPerSecond is < 1 or > 1000)
            errors.Add("RawgSettings:MaxRequestsPerSecond must be between 1 and 1000.");
        if (settings.RequestTimeoutSeconds is < 1 or > 120)
            errors.Add("RawgSettings:RequestTimeoutSeconds must be between 1 and 120.");
        if (settings.SearchCacheMinutes is < 1 or > 525600)
            errors.Add("RawgSettings:SearchCacheMinutes must be between 1 and 525600.");
        if (settings.DetailCacheMinutes is < 1 or > 525600)
            errors.Add("RawgSettings:DetailCacheMinutes must be between 1 and 525600.");
        // Report keys and rules only, never configuration values or parser exceptions.
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
