using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

public record LoginRequestDto
{
    [Required(ErrorMessage = "Email or username is required.")]
    public string Identifier { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; init; } = string.Empty;
}
