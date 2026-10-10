using Microsoft.AspNetCore.Identity;

namespace Playtrought.Api.Models;

/// <summary>Application user, keyed by <see cref="int"/>.</summary>
public class ApplicationUser : IdentityUser<int>
{
}