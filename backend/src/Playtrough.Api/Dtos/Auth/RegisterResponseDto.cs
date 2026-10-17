namespace Playtrough.Api.Dtos.Auth;

/// <summary>Public user representation returned after successful registration.
/// Never exposes the password hash, security stamp or any internal Identity data.</summary>
public record RegisterResponseDto(int Id, string Username, string Email, DateTime CreatedAt)
{
    public bool EmailConfirmationRequired => true;
}
