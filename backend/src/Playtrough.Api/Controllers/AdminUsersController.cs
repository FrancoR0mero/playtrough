using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Playtrough.Api.Authorization;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AppPolicies.RequireAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUsersController(IAdminUserService service) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers([FromQuery] UserListRequest request, CancellationToken ct)
        => Ok(await service.ListUsersAsync(request, ct));

}
