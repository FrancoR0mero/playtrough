using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Playtrough.Api.Dtos.Auth;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services;

// Runs before automatic model validation: even the two-letter reserved name "me"
// must return username_reserved, rather than a generic length validation error.
public sealed class ReservedUsernameFilter(IOptions<ProfileSettings> settings) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.Values.OfType<RegisterRequestDto>().FirstOrDefault() is { Username: { } username } &&
            UsernameRules.IsReserved(username.Trim(), settings.Value))
        {
            var problem = new ProblemDetails { Status = 400, Title = "Username reservado" };
            problem.Extensions["code"] = "username_reserved";
            context.Result = new BadRequestObjectResult(problem);
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
