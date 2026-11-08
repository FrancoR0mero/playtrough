using System.Text.RegularExpressions;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services;

public static partial class UsernameRules
{
    public const string Pattern = @"\A[a-zA-Z0-9_]{3,20}\z";
    [GeneratedRegex(Pattern)]
    private static partial Regex ValidUsername();
    public static bool IsValid(string? username) => username is not null && ValidUsername().IsMatch(username);
    public static bool IsReserved(string username, ProfileSettings settings)
        => settings.ReservedUsernames.Contains(username, StringComparer.OrdinalIgnoreCase);
}
