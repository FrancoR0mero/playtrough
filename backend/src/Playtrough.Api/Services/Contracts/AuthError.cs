namespace Playtrough.Api.Services.Contracts;

/// <summary>Outcome categories for authentication operations.</summary>
public enum AuthError
{
    None,
    EmailAlreadyExists,
    UsernameAlreadyExists,
    UsernameReserved,
    ValidationError,
    Unexpected,
    InvalidCredentials,
    AccountLocked,
    InvalidRefreshToken,
    InvalidPasswordReset,
    InvalidEmailConfirmation,
    EmailNotConfirmed,
}
