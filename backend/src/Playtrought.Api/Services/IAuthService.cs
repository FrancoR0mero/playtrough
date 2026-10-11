using Playtrought.Api.Dtos.Auth;

namespace Playtrought.Api.Services;

/// <summary>Use cases for authentication-related operations.</summary>
public interface IAuthService
{
    Task<ServiceResult<RegisterResponseDto>> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default);
}