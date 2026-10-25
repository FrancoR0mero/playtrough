namespace Playtrough.Api.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Register = "register";
    public const string PasswordRecovery = "password-recovery";
    public const string TokenActions = "token-actions";
    public const string Refresh = "refresh";
    public const string Catalog = "catalog";
    public const string Global = "global";
}
