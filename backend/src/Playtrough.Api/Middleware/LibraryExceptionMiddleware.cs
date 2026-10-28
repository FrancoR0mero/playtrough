using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Middleware;

public sealed class LibraryExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (LibraryException error) when (!context.Response.HasStarted)
        {
            var status = error.Code switch
            {
                "game_not_found" or "not_in_library" => StatusCodes.Status404NotFound,
                "already_in_library" or "concurrent_modification" => StatusCodes.Status409Conflict,
                "invalid_status" or "invalid_date" or "date_before_previous_event" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError,
            };
            var extensions = new Dictionary<string, object?> { ["code"] = status == 500 ? "internal_error" : error.Code };
            if (error.MinDate is { } minDate) extensions["minDate"] = minDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            await Results.Problem(statusCode: status, title: "No se pudo completar la operación de biblioteca",
                extensions: extensions).ExecuteAsync(context);
        }
    }
}
