using Microsoft.AspNetCore.Identity;

namespace Playtrough.Api.Models;

/// <summary>Application user, keyed by <see cref="int"/>.</summary>
public class ApplicationUser : IdentityUser<int>
{
    public string? Bio { get; set; }
    public DateTime? UsernameChangedAt { get; set; }
    public Visibility ProfileVisibility { get; set; } = Visibility.Public;
    public Visibility HistoryVisibility { get; set; } = Visibility.Friends;

    /// <summary>UTC timestamp of user creation, set by the application on insert.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
