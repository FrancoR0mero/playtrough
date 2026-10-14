using Playtrought.Api.Dtos.Auth;
using Playtrought.Api.Services.Contracts;

namespace Playtrought.Api.Services.Interfaces;

/// <summary>Use cases for authentication-related operations.</summary>
public interface IAuthService
{
    Task<ServiceResult<LoginResponseDto>> LoginAsync(LoginRequestDto dto, CancellationToken ct = default);

    Task<ServiceResult<RegisterResponseDto>> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default);
}
