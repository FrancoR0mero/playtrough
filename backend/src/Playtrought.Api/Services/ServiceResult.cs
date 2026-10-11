using Microsoft.AspNetCore.Identity;

namespace Playtrought.Api.Services;

/// <summary>Typed result for service operations; expected failures are values, not exceptions.</summary>
public sealed class ServiceResult<T>
{
    private ServiceResult(bool success, T? value, AuthError error, IReadOnlyList<IdentityError>? identityErrors)
    {
        Success = success;
        Value = value;
        Error = error;
        IdentityErrors = identityErrors ?? [];
    }

    public bool Success { get; }

    public T? Value { get; }

    public AuthError Error { get; }

    /// <summary>Identity errors returned by the store, when applicable (validation or unexpected).</summary>
    public IReadOnlyList<IdentityError> IdentityErrors { get; }

    public static ServiceResult<T> Ok(T value) => new(true, value, AuthError.None, null);

    public static ServiceResult<T> Fail(AuthError error, IReadOnlyList<IdentityError>? identityErrors = null)
        => new(false, default, error, identityErrors);
}