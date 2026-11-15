using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Authorization;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Controllers;

[ApiController, Route("api/admin/timeline"), Tags("Admin")]
[Authorize(Policy = AppPolicies.RequireAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 429)]
[ProducesResponseType(typeof(ProblemDetails), 500)]
public sealed class AdminTimelineController(TimelineBackfillService backfill) : ControllerBase
{
    /// <summary>Crear las entradas históricas faltantes en lotes de hasta 500.</summary>
    /// <remarks>Solo Admin. Repetible sin duplicar; excluye las bajas de biblioteca.
    /// Cada lote es atómico. Si se interrumpe, puede volver a ejecutarse.</remarks>
    [HttpPost("backfill")]
    [ProducesResponseType(typeof(TimelineBackfillResult), 200)]
    public async Task<IActionResult> Backfill(CancellationToken ct) => Ok(await backfill.RunAsync(ct));
}
