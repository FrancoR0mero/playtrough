using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Middleware;

public sealed class FriendExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (FriendException error) when (!context.Response.HasStarted)
        {
            var status = error.Code switch
            {
                "user_not_found" or "request_not_found" => 404,
                "cannot_befriend_self" or "invalid_query" => 400,
                _ => 409,
            };
            var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
            if (error.AvailableAt is { } available) extensions["availableAt"] = available;
            if (error.RequestId is { } id) extensions["requestId"] = id;
            await Results.Problem(statusCode: status, title: "No se pudo completar la operación de amistad",
                extensions: extensions).ExecuteAsync(context);
        }
    }
}
