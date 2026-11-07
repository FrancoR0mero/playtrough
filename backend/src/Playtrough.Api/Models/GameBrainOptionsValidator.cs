using Microsoft.Extensions.Options;

namespace Playtrough.Api.Models;

public sealed class GameBrainOptionsValidator : IValidateOptions<GameBrainOptions>
{
    public ValidateOptionsResult Validate(string? name, GameBrainOptions settings)
    {
        var errors = new List<string>();
        if (settings.Enabled && string.IsNullOrWhiteSpace(settings.ApiKey))
            errors.Add("GameBrain:ApiKey is required when GameBrain:Enabled is true.");
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            errors.Add("GameBrain:BaseUrl must be an absolute HTTPS URL without credentials, query or fragment.");
        if (settings.RequestTimeoutSeconds is < 1 or > 120)
            errors.Add("GameBrain:RequestTimeoutSeconds must be between 1 and 120.");
        if (settings.SearchCacheMinutes is < 1 or > 1440)
            errors.Add("GameBrain:SearchCacheMinutes must be between 1 and 1440.");
        if (settings.DetailCacheMinutes is < 1 or > 1440)
            errors.Add("GameBrain:DetailCacheMinutes must be between 1 and 1440.");
        if (settings.OffersCacheMinutes is < 1 or > 60)
            errors.Add("GameBrain:OffersCacheMinutes must be between 1 and 60.");
        if (settings.ApiKey.Any(char.IsControl))
            errors.Add("GameBrain:ApiKey must not contain control characters.");
        // Report keys and rules only, never configuration values or parser exceptions.
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
