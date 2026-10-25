namespace Playtrough.Api.Models;

public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimit";

    public bool Enabled { get; set; } = true;
    public RateLimitPolicySettings Login { get; set; } = new(10, TimeSpan.FromMinutes(1));
    public RateLimitPolicySettings Register { get; set; } = new(5, TimeSpan.FromHours(1));
    public RateLimitPolicySettings PasswordRecovery { get; set; } = new(5, TimeSpan.FromMinutes(15));
    public RateLimitPolicySettings TokenActions { get; set; } = new(10, TimeSpan.FromMinutes(15));
    public RateLimitPolicySettings Refresh { get; set; } = new(30, TimeSpan.FromMinutes(1));
    public RateLimitPolicySettings Catalog { get; set; } = new(30, TimeSpan.FromMinutes(1));
    public RateLimitPolicySettings Global { get; set; } = new(100, TimeSpan.FromMinutes(1));
}

public sealed class RateLimitPolicySettings
{
    public RateLimitPolicySettings() { }

    public RateLimitPolicySettings(int permitLimit, TimeSpan window)
    {
        PermitLimit = permitLimit;
        Window = window;
    }

    public int PermitLimit { get; set; }
    public TimeSpan Window { get; set; }
}
