using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Playtrought.Api.Dtos.Auth;
using Playtrought.Api.Services;

namespace Playtrought.Api.Controllers;

/// <summary>Authentication endpoints. Maps service results to HTTP responses.</summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
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
