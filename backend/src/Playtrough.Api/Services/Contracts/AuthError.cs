namespace Playtrough.Api.Services.Contracts;

/// <summary>Outcome categories for authentication operations.</summary>
public enum AuthError
{
    None,
    EmailAlreadyExists,
    UsernameAlreadyExists,
    ValidationError,
    Unexpected,
    InvalidCredentials,
    AccountLocked,
    InvalidRefreshToken,
}
