using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

/// <summary>Use cases for authentication-related operations.</summary>
public interface IAuthService
{
    Task<ServiceResult<AuthMessageDto>> ConfirmEmailAsync(ConfirmEmailRequestDto dto, CancellationToken ct = default);
    Task<AuthMessageDto> ResendConfirmationAsync(ResendConfirmationRequestDto dto, CancellationToken ct = default);
    Task ProcessResendConfirmationAsync(string email, CancellationToken ct = default);

    Task<AuthMessageDto> ForgotPasswordAsync(ForgotPasswordRequestDto dto, CancellationToken ct = default);

    Task<ServiceResult<AuthMessageDto>> ResetPasswordAsync(ResetPasswordRequestDto dto, CancellationToken ct = default);

    // Invoked only by the background worker in a new service scope, not exposed as an endpoint.
    Task ProcessForgotPasswordAsync(string email, CancellationToken ct = default);

    Task<ServiceResult<RefreshResponseDto>> RefreshAsync(RefreshRequestDto dto, CancellationToken ct = default);

    Task LogoutAsync(int userId, LogoutRequestDto dto, CancellationToken ct = default);

    Task<ServiceResult<LoginResponseDto>> LoginAsync(LoginRequestDto dto, CancellationToken ct = default);

    Task<ServiceResult<RegisterResponseDto>> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default);
}
