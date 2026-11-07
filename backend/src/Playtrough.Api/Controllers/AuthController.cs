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

/// <summary>Registro, autenticación y recuperación de cuentas.</summary>
[ApiController]
[Route("api/[controller]")]
[Tags("Auth")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Confirmar el email de la cuenta.</summary>
    /// <remarks>Endpoint público. Enviá el email y el token del enlace de confirmación.</remarks>
    /// <response code="200">Email confirmado; también se acepta una confirmación ya completada.</response>
    /// <response code="400">Datos inválidos o enlace de confirmación inválido o vencido.</response>
    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitPolicies.TokenActions)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AuthMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequestDto dto, CancellationToken ct)
    {
        var result = await authService.ConfirmEmailAsync(dto, ct);
        return result.Success ? Ok(result.Value)
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid email confirmation request",
                detail: "The email confirmation link is invalid or has expired.");
    }

    /// <summary>Solicitar el reenvío del correo de confirmación.</summary>
    /// <remarks>Endpoint público. No revela si el email existe o ya fue confirmado; el envío respeta el cooldown configurado.</remarks>
    /// <response code="200">Respuesta neutra, independiente de la existencia o estado de la cuenta.</response>
    /// <response code="400">Email ausente o con formato inválido.</response>
    [HttpPost("resend-confirmation")]
    [EnableRateLimiting(RateLimitPolicies.PasswordRecovery)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AuthMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequestDto dto, CancellationToken ct)
        => Ok(await authService.ResendConfirmationAsync(dto, ct));

    /// <summary>Restablecer la contraseña con el token recibido por email.</summary>
    /// <remarks>Endpoint público. Un reset exitoso revoca los refresh tokens y elimina el bloqueo de la cuenta.</remarks>
    /// <response code="200">Contraseña actualizada.</response>
    /// <response code="400">Datos inválidos, contraseña fuera de la política o enlace inválido, vencido o ya utilizado.</response>
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.TokenActions)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AuthMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
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

    /// <summary>Solicitar un correo de recuperación de contraseña.</summary>
    /// <remarks>Endpoint público. El procesamiento se realiza en segundo plano y la respuesta no revela si existe la cuenta.</remarks>
    /// <response code="200">Respuesta neutra para cuentas existentes, desconocidas o bloqueadas.</response>
    /// <response code="400">Email ausente o con formato inválido.</response>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.PasswordRecovery)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AuthMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
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

    /// <summary>Renovar la sesión y rotar el refresh token.</summary>
    /// <response code="200">Nuevos tokens y datos actuales del usuario.</response>
    /// <response code="400">Refresh token requerido ausente o vacío.</response>
    /// <response code="401">Refresh token inválido o usuario no habilitado; respuesta genérica.</response>
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

    /// <summary>Cerrar la sesión revocando su refresh token.</summary>
    /// <remarks>Requiere un access token válido. La revocación es idempotente y solo afecta a la sesión indicada.</remarks>
    /// <response code="204">Sesión revocada o token ya revocado, desconocido o ajeno al usuario.</response>
    /// <response code="400">Refresh token requerido ausente o vacío.</response>
    /// <response code="401">Access token ausente o inválido.</response>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
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

    /// <summary>Iniciar sesión con email o nombre de usuario.</summary>
    /// <response code="403">Email sin confirmar; extensions.code: email_not_confirmed.</response>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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

    /// <summary>Registrar una cuenta y solicitar la confirmación del email.</summary>
    /// <remarks>Endpoint público. La cuenta se crea con rol User y no recibe tokens hasta iniciar sesión con el email confirmado.</remarks>
    /// <response code="201">Cuenta creada; emailConfirmationRequired indica que se debe confirmar el email.</response>
    /// <response code="400">Datos inválidos o contraseña fuera de la política configurada.</response>
    /// <response code="409">El email o el nombre de usuario ya están registrados.</response>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
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
