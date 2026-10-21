using Microsoft.AspNetCore.Identity;

namespace Playtrough.Api.Models;

/// <summary>Application user, keyed by <see cref="int"/>.</summary>
public class ApplicationUser : IdentityUser<int>
{
    /// <summary>UTC timestamp of user creation, set by the application on insert.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}