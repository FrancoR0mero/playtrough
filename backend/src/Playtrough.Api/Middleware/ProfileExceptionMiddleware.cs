using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Middleware;

public sealed class ProfileExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (ProfileException error) when (!context.Response.HasStarted)
        {
            var status = error.Code switch
            {
                "user_not_found" => 404,
                "username_taken" or "username_change_too_soon" or "concurrent_modification" => 409,
                _ => 400,
            };
            var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
            if (error.AvailableAt is { } available) extensions["availableAt"] = available;
            await Results.Problem(statusCode: status, title: "No se pudo completar la operación de perfil",
                extensions: extensions).ExecuteAsync(context);
        }
    }
}
