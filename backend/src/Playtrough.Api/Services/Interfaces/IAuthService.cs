using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Interfaces;

/// <summary>Use cases for authentication-related operations.</summary>
public interface IAuthService
{
    Task<ServiceResult<RefreshResponseDto>> RefreshAsync(RefreshRequestDto dto, CancellationToken ct = default);

    Task LogoutAsync(int userId, LogoutRequestDto dto, CancellationToken ct = default);

    Task<ServiceResult<LoginResponseDto>> LoginAsync(LoginRequestDto dto, CancellationToken ct = default);

    Task<ServiceResult<RegisterResponseDto>> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default);
}
