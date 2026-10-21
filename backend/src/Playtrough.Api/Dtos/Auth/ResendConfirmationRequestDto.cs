using System.ComponentModel.DataAnnotations;

namespace Playtrough.Api.Dtos.Auth;

public sealed class ResendConfirmationRequestDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}
