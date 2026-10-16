using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Text;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

/// <summary>Registration, login, logout and refresh use cases. Identity handles passwords and lockout;
/// token generation is delegated to ITokenService.</summary>
public class AuthService(
    IUserRepository userRepository,
    ILogger<AuthService> logger,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService,
    IRefreshTokenRepository refreshTokens,
    IEmailSender emailSender,
    IPasswordResetQueue passwordResetQueue,
    IOptions<PasswordResetSettings> passwordResetOptions) : IAuthService
{
    private const string UserRole = "User";

    public async Task<ServiceResult<AuthMessageDto>> ResetPasswordAsync(ResetPasswordRequestDto dto, CancellationToken ct = default)
    {
        string decoded;
        try { decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token)); }
        catch (FormatException) { return ServiceResult<AuthMessageDto>.Fail(AuthError.InvalidPasswordReset); }

        try
        {
            await using var transaction = await userRepository.BeginTransactionAsync(ct);
            var user = await userRepository.FindByEmailAsync(dto.Email.Trim(), ct);
            if (user is null) return ServiceResult<AuthMessageDto>.Fail(AuthError.InvalidPasswordReset);
            var users = signInManager.UserManager;
            var reset = await users.ResetPasswordAsync(user, decoded, dto.NewPassword);
            if (!reset.Succeeded)
            {
                var passwordErrors = reset.Errors.Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal)).ToList();
                return passwordErrors.Count > 0
                    ? ServiceResult<AuthMessageDto>.Fail(AuthError.ValidationError, passwordErrors)
                    : ServiceResult<AuthMessageDto>.Fail(AuthError.InvalidPasswordReset);
            }
            var clearAttempts = await users.ResetAccessFailedCountAsync(user);
            if (!clearAttempts.Succeeded) return ServiceResult<AuthMessageDto>.Fail(AuthError.Unexpected);
            if (user.LockoutEnd is not null)
            {
                var unlock = await users.SetLockoutEndDateAsync(user, null);
                if (!unlock.Succeeded) return ServiceResult<AuthMessageDto>.Fail(AuthError.Unexpected);
            }
            await refreshTokens.RevokeAllActiveAsync(user.Id, DateTime.UtcNow, ct);
            await transaction.CommitAsync(ct);
            return ServiceResult<AuthMessageDto>.Ok(new AuthMessageDto("La contraseña se actualizó correctamente."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError("Password reset failed ({ExceptionType}).", ex.GetType().Name);
            return ServiceResult<AuthMessageDto>.Fail(AuthError.Unexpected);
        }
    }

    public Task<AuthMessageDto> ForgotPasswordAsync(ForgotPasswordRequestDto dto, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Every valid request does the same work here, including unknown/locked accounts.
        // Lookup, token generation and SMTP are deferred to the worker's own DI scope.
        passwordResetQueue.Enqueue(dto.Email.Trim());
        return Task.FromResult(new AuthMessageDto(
            "Si el correo corresponde a una cuenta habilitada, vas a recibir instrucciones para restablecer tu contraseña."));
    }

    public async Task ProcessForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        try
        {
            var user = await userRepository.FindByEmailAsync(email, ct);
            var users = signInManager.UserManager;
            if (user is null || await users.IsLockedOutAsync(user)) return;
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var settings = passwordResetOptions.Value;
            var url = $"{settings.FrontendBaseUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={encoded}";
            var content = PasswordResetEmailTemplate.Create(user.UserName ?? "gamer", url, settings.TokenLifespanMinutes);
            await emailSender.SendAsync(user.Email!, content.Subject, content.HtmlBody, content.TextBody, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // SMTP exception messages can contain recipient addresses, credentials or message data.
            logger.LogError("Password recovery email processing failed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    public async Task<ServiceResult<RefreshResponseDto>> RefreshAsync(RefreshRequestDto dto, CancellationToken ct = default)
    {
        try
        {
            var hash = tokenService.HashRefreshToken(dto.RefreshToken);
            var original = await refreshTokens.FindByHashAsync(hash, ct);
            if (original is null)
                return ServiceResult<RefreshResponseDto>.Fail(AuthError.InvalidRefreshToken);

            await using var transaction = await userRepository.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;

            // The first SQL statement inside the transaction is a conditional write, not a read.
            // Concurrent requests wait on the same row. A losing request then reads the winner's
            // committed replacement, without a stale MySQL REPEATABLE READ snapshot.
            var consumed = await refreshTokens.TryRevokeActiveAsync(hash, original.UserId, now, ct);
            if (!consumed)
            {
                var current = await refreshTokens.FindByHashAsync(hash, ct);
                if (current?.RevokedAt is not null && current.ReplacedByToken is not null)
                {
                    await refreshTokens.RevokeAllActiveAsync(current.UserId, DateTime.UtcNow, ct);
                    logger.LogWarning("Refresh token reuse detected for user {UserId}; active refresh tokens revoked.", current.UserId);
                }
                await transaction.CommitAsync(ct);
                return ServiceResult<RefreshResponseDto>.Fail(AuthError.InvalidRefreshToken);
            }

            // Check the original entity with the shared rule; the conditional UPDATE above
            // also checks expiry/revocation in SQL so a stale pre-transaction read cannot win.
            if (!original.IsActive(now))
                return ServiceResult<RefreshResponseDto>.Fail(AuthError.InvalidRefreshToken);

            var user = await userRepository.FindByIdAsync(original.UserId, ct);
            if (user is null || !await signInManager.CanSignInAsync(user) ||
                await signInManager.UserManager.IsLockedOutAsync(user))
                return ServiceResult<RefreshResponseDto>.Fail(AuthError.InvalidRefreshToken);

            var roles = await userRepository.GetRolesAsync(user, ct);
            var tokens = await tokenService.CreateAsync(user, roles, ct);
            await refreshTokens.SetReplacementAsync(hash, tokenService.HashRefreshToken(tokens.RefreshToken), ct);
            var response = new RefreshResponseDto(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt,
                ToLoginUser(user, roles));
            await transaction.CommitAsync(ct);
            return ServiceResult<RefreshResponseDto>.Ok(response);
            // Disposing an uncommitted transaction rolls back both consumption and token insertion.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never log tokens, hashes or exception text that could contain SQL parameters.
            logger.LogError("Unexpected failure during refresh ({ExceptionType}).", ex.GetType().Name);
            return ServiceResult<RefreshResponseDto>.Fail(AuthError.Unexpected);
        }
    }

    private static LoginUserDto ToLoginUser(ApplicationUser user, IList<string> roles)
    {
        var role = roles.Contains("Admin") ? "Admin" : roles.OrderBy(r => r, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
        return new LoginUserDto(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, role);
    }

    public Task LogoutAsync(int userId, LogoutRequestDto dto, CancellationToken ct = default)
    {
        // Design decision: logout revokes this refresh token only. Issued access JWTs
        // remain valid until their configured expiry (15 minutes by default).
        var tokenHash = tokenService.HashRefreshToken(dto.RefreshToken);
        return refreshTokens.RevokeActiveAsync(tokenHash, userId, DateTime.UtcNow, ct);
    }

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
            return ServiceResult<LoginResponseDto>.Ok(new LoginResponseDto(
                tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresAt,
                ToLoginUser(user, roles)));
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
