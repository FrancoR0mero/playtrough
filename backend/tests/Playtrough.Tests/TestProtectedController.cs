using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Tests;

/// <summary>Available only in LoginApiFactory; uses the real bearer authentication handler.</summary>
[ApiController]
[Route("__tests/protected")]
[Authorize(Roles = "User")]
public class TestProtectedController : ControllerBase
{
    [HttpGet]
    public ActionResult<ProtectedUserResponse> Get() => Ok(new ProtectedUserResponse(
        User.FindFirst("sub")?.Value,
        User.Identity?.Name,
        User.FindFirst("email")?.Value,
        User.FindFirst("role")?.Value));
}

public record ProtectedUserResponse(string? Subject, string? Username, string? Email, string? Role);
