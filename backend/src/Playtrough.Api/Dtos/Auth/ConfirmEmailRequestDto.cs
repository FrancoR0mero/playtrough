using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

public sealed class ConfirmEmailRequestDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Token { get; set; } = string.Empty;
}
