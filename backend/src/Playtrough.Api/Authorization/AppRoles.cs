namespace Playtrough.Api.Authorization;

public static class AppRoles
{
    public const string User = "User";
    public const string Moderator = "Moderator";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { User, Moderator, Admin });
    public static bool IsValid(string role) => All.Contains(role, StringComparer.Ordinal);
}
