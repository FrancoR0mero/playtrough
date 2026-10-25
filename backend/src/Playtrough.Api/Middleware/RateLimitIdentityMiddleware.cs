using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;
using Playtrough.Api.RateLimiting;

namespace Playtrough.Api.Middleware;

/// <summary>Resolves the global/catalog partition identity before UseAuthentication.</summary>
public sealed class RateLimitIdentityMiddleware(RequestDelegate next, IOptions<RateLimitSettings> settings)
{
    internal static readonly object UserPartitionKey = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var policy = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (settings.Value.Enabled &&
            (policy is null || policy == RateLimitPolicies.Catalog) &&
            endpoint?.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null)
        {
            // Never read an unvalidated token's subject. The real JWT handler validates
            // signature, issuer, audience and lifetime, and caches its result per request.
            // UseAuthentication later reuses that result and sets HttpContext.User normally.
            var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (result.Succeeded && result.Principal?.Identity?.IsAuthenticated == true &&
                int.TryParse(result.Principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                    NumberStyles.None, CultureInfo.InvariantCulture, out var userId) && userId > 0)
                context.Items[UserPartitionKey] = userId.ToString(CultureInfo.InvariantCulture);
        }
        await next(context);
    }
}
