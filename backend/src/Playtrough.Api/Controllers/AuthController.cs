using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Playtrough.Api.RateLimiting;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

/// <summary>Authentication endpoints. Maps service results to HTTP responses.</summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitPolicies.TokenActions)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequestDto dto, CancellationToken ct)
    {
        var result = await authService.ConfirmEmailAsync(dto, ct);
        return result.Success ? Ok(result.Value)
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid email confirmation request",
                detail: "The email confirmation link is invalid or has expired.");
    }

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting(RateLimitPolicies.PasswordRecovery)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequestDto dto, CancellationToken ct)
        => Ok(await authService.ResendConfirmationAsync(dto, ct));

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.TokenActions)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResetPassword(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResetPasswordRequestDto? dto, CancellationToken ct)
    {
        if (dto is null)
        {
            ModelState.AddModelError(string.Empty, "A request body is required.");
            return ValidationProblem(ModelState);
        }
        var result = await authService.ResetPasswordAsync(dto, ct);
        if (result.Success) return Ok(result.Value);
        if (result.Error == AuthError.InvalidPasswordReset)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid password reset request",
                detail: "The password reset link is invalid or has expired.");
        if (result.Error == AuthError.ValidationError)
        {
            var errors = new ModelStateDictionary();
            foreach (var error in result.IdentityErrors)
                errors.AddModelError(nameof(ResetPasswordRequestDto.NewPassword), error.Description);
            return ValidationProblem(errors);
        }
        return Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred while resetting the password.");
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.PasswordRecovery)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ForgotPasswordRequestDto? dto, CancellationToken ct)
    {
        if (dto is null)
        {
            ModelState.AddModelError(nameof(ForgotPasswordRequestDto.Email), "Email is required.");
            return ValidationProblem(ModelState);
        }
        return Ok(await authService.ForgotPasswordAsync(dto, ct));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(RefreshResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequestDto? dto, CancellationToken ct)
    {
        if (dto is null)
        {
            ModelState.AddModelError(nameof(RefreshRequestDto.RefreshToken), "Refresh token is required.");
            return ValidationProblem(ModelState);
        }
        var result = await authService.RefreshAsync(dto, ct);
        if (result.Success) return Ok(result.Value);
        return result.Error == AuthError.InvalidRefreshToken
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token",
                detail: "The refresh token is invalid or cannot be used.")
            : Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred while refreshing the session.");
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto dto, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
        {
            return Challenge();
        }

        await authService.LogoutAsync(userId, dto, ct);
        return NoContent();
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto, CancellationToken ct)
    {
        var result = await authService.LoginAsync(dto, ct);
        if (result.Success)
            return Ok(result.Value);

        return result.Error switch
        {
            AuthError.EmailNotConfirmed => Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Email not confirmed", detail: "Confirm your email before logging in.",
                extensions: new Dictionary<string, object?> { ["code"] = "email_not_confirmed" }),
            AuthError.InvalidCredentials => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "The email/username or password is incorrect."),
            AuthError.AccountLocked => Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account locked",
                detail: "This account is temporarily locked after too many failed login attempts. Try again in 15 minutes."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred while logging in."),
        };
    }

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(dto, ct);
        if (result.Success)
        {
            return Created("/api/auth/register", result.Value);
        }

        return result.Error switch
        {
            AuthError.EmailAlreadyExists => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Email already registered",
                Detail = "An account with this email already exists.",
            }),
            AuthError.UsernameAlreadyExists => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Username already taken",
                Detail = "An account with this username already exists.",
            }),
            AuthError.ValidationError => ValidationProblem(ToModelState(result)),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred while registering the account."),
        };
    }

    /// <summary>Folds Identity registration errors into the Password field of ModelState.</summary>
    private static ModelStateDictionary ToModelState(ServiceResult<RegisterResponseDto> result)
    {
        var modelState = new ModelStateDictionary();
        foreach (var error in result.IdentityErrors)
        {
            modelState.AddModelError(nameof(RegisterRequestDto.Password), error.Description);
        }

        return modelState;
    }
}
