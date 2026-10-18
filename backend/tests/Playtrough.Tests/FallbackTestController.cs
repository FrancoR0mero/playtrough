using Microsoft.AspNetCore.Mvc;

namespace Playtrough.Tests;

/// <summary>Registered only by the test factory. Intentionally has no authorization attributes.</summary>
[ApiController]
[Route("__tests/fallback")]
public class FallbackTestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "Authenticated by fallback policy." });
}
