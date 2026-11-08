using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Models;

public sealed class ProfileSettings
{
    public const string SectionName = "Profile";
    public Visibility DefaultProfileVisibility { get; set; } = Visibility.Public;
    public Visibility DefaultHistoryVisibility { get; set; } = Visibility.Friends;
    [Range(1, 36500)]
    public int UsernameChangeCooldownDays { get; set; } = 30;
    [Required]
    public string[] ReservedUsernames { get; set; } =
        ["me", "search", "admin", "administrator", "moderator", "support", "system", "api", "null", "undefined", "root"];
}
