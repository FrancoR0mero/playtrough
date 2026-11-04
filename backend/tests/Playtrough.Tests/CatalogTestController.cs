using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Tests;

// Only the test assembly registers these probes. Public /api/games routes belong to phase 3.
[ApiController]
[NonController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("__tests/catalog")]
public sealed class CatalogTestController(IGameCatalogProvider provider) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search(string q = "witcher", int page = 1, int pageSize = 20)
        => Ok(await provider.SearchAsync(q, page, pageSize, HttpContext.RequestAborted));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
        => Ok(await provider.GetByExternalIdAsync(id, HttpContext.RequestAborted));

    [HttpGet("local/{id:int}")]
    public async Task<IActionResult> LocalDetail(int id, [FromServices] IGameService games)
        => Ok(await games.GetOrCreateAsync("rawg", id, HttpContext.RequestAborted));
}

internal sealed class CatalogControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        => feature.Controllers.Add(typeof(CatalogTestController).GetTypeInfo());
}
