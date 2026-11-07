using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;

namespace Playtrough.Tests;

// Loaded only as a test application part by LoginApiFactory.
[ApiController]
[NonController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("__tests/security")]
public sealed class SecurityTestController : ControllerBase
{
    public const string SensitiveMarker = "test-sensitive-canary";

    [HttpGet("connection")]
    public IActionResult Connection() => Ok(new
    {
        ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
        scheme = Request.Scheme,
    });

    [HttpGet("throw")]
    public IActionResult Throw()
    {
        try { ThrowInner(); }
        catch (Exception inner)
        {
            var error = new InvalidOperationException($"Password={SensitiveMarker}; Authorization: Bearer {SensitiveMarker}", inner);
            error.Data["connection"] = $"Server=localhost;Password={SensitiveMarker}";
            throw error;
        }
        return NoContent();
    }

    private static void ThrowInner() => throw new IOException(
        $"https://frontend.example/reset-password?token={SensitiveMarker}");
}

internal sealed class SecurityControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
        feature.Controllers.Add(typeof(SecurityTestController).GetTypeInfo());
}
