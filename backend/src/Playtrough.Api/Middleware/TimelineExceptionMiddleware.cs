using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Middleware;

public sealed class TimelineExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (TimelineException error) when (!context.Response.HasStarted)
        {
            var status = error.Code switch
            {
                "user_not_found" or "entry_not_found" => 404,
                "history_restricted" => 403,
                "invalid_cursor" or "invalid_filter" or "invalid_body" => 400,
                _ => 500,
            };
            var extensions = new Dictionary<string, object?> { ["code"] = status == 500 ? "internal_error" : error.Code };
            if (error.Restriction is { } restriction) extensions["restriction"] = restriction;
            await Results.Problem(statusCode: status, title: "No se pudo completar la operación de línea de tiempo",
                extensions: extensions).ExecuteAsync(context);
        }
    }
}
