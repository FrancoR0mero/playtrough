using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

/// <summary>Payload for POST /api/auth/register.</summary>
public record RegisterRequestDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email address is required.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Username is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 20 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_.]{3,20}$",
        ErrorMessage = "Username may contain only letters, digits, underscore and dot.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; init; } = string.Empty;
}