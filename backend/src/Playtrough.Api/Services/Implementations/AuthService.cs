using Microsoft.AspNetCore.Identity;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Registration and login use cases. Identity handles passwords and lockout;
/// token generation is delegated to ITokenService.</summary>
public class AuthService(
    IUserRepository userRepository,
    ILogger<AuthService> logger,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService) : IAuthService
{
    private const string UserRole = "User";

    public async Task<ServiceResult<LoginResponseDto>> LoginAsync(LoginRequestDto dto, CancellationToken ct = default)
    {
        try
        {
            var identifier = dto.Identifier.Trim();
            var user = identifier.Contains('@')
                ? await userRepository.FindByEmailAsync(identifier, ct)
                : await userRepository.FindByUsernameAsync(identifier, ct);

            if (user is null)
                return ServiceResult<LoginResponseDto>.Fail(AuthError.InvalidCredentials);

            var signIn = await signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
            if (signIn.IsLockedOut)
                return ServiceResult<LoginResponseDto>.Fail(AuthError.AccountLocked);

            if (!signIn.Succeeded)
                return ServiceResult<LoginResponseDto>.Fail(AuthError.InvalidCredentials);

            var reset = await userRepository.ResetAccessFailedCountAsync(user, ct);
            if (!reset.Succeeded)
                return ServiceResult<LoginResponseDto>.Fail(AuthError.Unexpected);

            var roles = await userRepository.GetRolesAsync(user, ct);
            var tokens = await tokenService.CreateAsync(user, roles, ct);
            var role = roles.Contains("Admin") ? "Admin" : roles.OrderBy(r => r, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;

            return ServiceResult<LoginResponseDto>.Ok(new LoginResponseDto(
                tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt,
                new LoginUserDto(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, role)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure during login");
            return ServiceResult<LoginResponseDto>.Fail(AuthError.Unexpected);
        }
    }

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
