using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

public record LogoutRequestDto
{
    [Required(ErrorMessage = "Refresh token is required.")]
    public string RefreshToken { get; init; } = string.Empty;
}
