using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Playtrought.Api.Dtos.Auth;
using Playtrought.Api.Services;

namespace Playtrought.Api.Controllers;

/// <summary>Registration endpoint. No business logic: maps service results to HTTP responses.</summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
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