using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Playtrough.Api.Middleware;
using Playtrough.Api.Models;

namespace Playtrough.Api.RateLimiting;

public sealed class ConfigureRateLimiting(
    IOptions<RateLimitSettings> settings, ILogger<ConfigureRateLimiting> logger) : IConfigureOptions<RateLimiterOptions>
{
    public void Configure(RateLimiterOptions options)
    {
        var limits = settings.Value;
        AddPolicy(options, RateLimitPolicies.Login, limits.Login, limits.Enabled);
        AddPolicy(options, RateLimitPolicies.Register, limits.Register, limits.Enabled);
        AddPolicy(options, RateLimitPolicies.PasswordRecovery, limits.PasswordRecovery, limits.Enabled);
        AddPolicy(options, RateLimitPolicies.TokenActions, limits.TokenActions, limits.Enabled);
        AddPolicy(options, RateLimitPolicies.Refresh, limits.Refresh, limits.Enabled);

        if (limits.Enabled)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Auth endpoints have their own budgets, including the shared recovery
                // and token-action budgets. The global budget covers the rest only.
                if (context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
                    return RateLimitPartition.GetNoLimiter("endpoint-policy");
                var key = context.Items.TryGetValue(RateLimitIdentityMiddleware.UserPartitionKey, out var userId)
                    ? $"user:{userId}" : $"ip:{RemoteIp(context)}";
                return FixedWindow(key, limits.Global);
            });

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (rejection, _) =>
        {
            var context = rejection.HttpContext;
            var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
                ?? RateLimitPolicies.Global;
            // Fixed windows provide RetryAfter. Round up so even subsecond test
            // windows produce a useful, positive Retry-After value in seconds.
            var wait = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? retryAfter : WindowFor(policy, limits);
            context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(wait.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
            logger.LogWarning("Rate limit exceeded. Policy: {Policy}. IP: {IpAddress}.", policy, RemoteIp(context));
            await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: "Demasiadas solicitudes",
                detail: "Se alcanzó el límite de solicitudes. Intentá nuevamente más tarde.",
                extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" }).ExecuteAsync(context);
        };
    }

    private static void AddPolicy(RateLimiterOptions options, string name, RateLimitPolicySettings policy, bool enabled)
        => options.AddPolicy(name, context => enabled
            ? FixedWindow(RemoteIp(context), policy)
            : RateLimitPartition.GetNoLimiter<string>(name));

    private static RateLimitPartition<string> FixedWindow(string key, RateLimitPolicySettings policy)
        => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = policy.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static string RemoteIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        // A peer represented as IPv4-mapped IPv6 must share its IPv4 budget.
        return (ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4() : ip)?.ToString() ?? "unknown";
    }

    private static TimeSpan WindowFor(string policy, RateLimitSettings limits) => policy switch
    {
        RateLimitPolicies.Login => limits.Login.Window,
        RateLimitPolicies.Register => limits.Register.Window,
        RateLimitPolicies.PasswordRecovery => limits.PasswordRecovery.Window,
        RateLimitPolicies.TokenActions => limits.TokenActions.Window,
        RateLimitPolicies.Refresh => limits.Refresh.Window,
        _ => limits.Global.Window,
    };
}
