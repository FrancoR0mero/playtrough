using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

public sealed class RefreshRequestDto
{
    [Required(ErrorMessage = "Refresh token is required.")]
    public string RefreshToken { get; set; } = string.Empty;
}
