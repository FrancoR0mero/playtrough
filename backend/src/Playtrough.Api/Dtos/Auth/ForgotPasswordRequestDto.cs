using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

public sealed class ForgotPasswordRequestDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email address is required.")]
    public string Email { get; set; } = string.Empty;
}
