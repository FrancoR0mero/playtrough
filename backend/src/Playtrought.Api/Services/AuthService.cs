using Microsoft.AspNetCore.Identity;
using Playtrought.Api.Dtos.Auth;
using Playtrought.Api.Models;
using Playtrought.Api.Repositories;

namespace Playtrought.Api.Services;

/// <summary>Registration use case: unique email/username, Identity-hashed password, role assignment.
/// Create-user and add-role run inside one transaction; any failure rolls both back.</summary>
public class AuthService(
    IUserRepository userRepository,
    ILogger<AuthService> logger) : IAuthService
{
    private const string UserRole = "User";

    public async Task<ServiceResult<RegisterResponseDto>> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default)
    {
        var email = dto.Email.Trim();
        var username = dto.Username.Trim();

        if (await userRepository.ExistsByEmailAsync(email, ct))
        {
            return ServiceResult<RegisterResponseDto>.Fail(AuthError.EmailAlreadyExists);
        }

        if (await userRepository.ExistsByUsernameAsync(username, ct))
        {
            return ServiceResult<RegisterResponseDto>.Fail(AuthError.UsernameAlreadyExists);
        }

        var transaction = await userRepository.BeginTransactionAsync(ct);
        try
        {
            var user = new ApplicationUser { UserName = username, Email = email };
            var createResult = await userRepository.CreateAsync(user, dto.Password, ct);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                if (createResult.Errors.Any(e => e.Code == "DuplicateEmail"))
                {
                    return ServiceResult<RegisterResponseDto>.Fail(AuthError.EmailAlreadyExists);
                }

                if (createResult.Errors.Any(e => e.Code == "DuplicateUserName"))
                {
                    return ServiceResult<RegisterResponseDto>.Fail(AuthError.UsernameAlreadyExists);
                }

                return ServiceResult<RegisterResponseDto>.Fail(AuthError.ValidationError, createResult.Errors.ToList());
            }

            var roleResult = await userRepository.AddToRoleAsync(user, UserRole, ct);
            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return ServiceResult<RegisterResponseDto>.Fail(AuthError.Unexpected, roleResult.Errors.ToList());
            }

            await transaction.CommitAsync(ct);
            return ServiceResult<RegisterResponseDto>.Ok(user.ToRegisterResponse());
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "Unexpected failure during registration for username '{Username}'", username);
            return ServiceResult<RegisterResponseDto>.Fail(AuthError.Unexpected);
        }
    }
}