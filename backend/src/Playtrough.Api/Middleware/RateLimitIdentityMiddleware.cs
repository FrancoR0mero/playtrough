using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;

namespace Playtrough.Api.Middleware;

/// <summary>Resolves only the global partition's identity before UseAuthentication.</summary>
public sealed class RateLimitIdentityMiddleware(RequestDelegate next, IOptions<RateLimitSettings> settings)
{
    internal static readonly object UserPartitionKey = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (settings.Value.Enabled &&
            endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null &&
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
