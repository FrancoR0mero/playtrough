using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Middleware;

public sealed class ReviewExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (ReviewException error) when (!context.Response.HasStarted)
        {
            var status = error.Code switch
            {
                "game_not_found" or "review_not_found" => StatusCodes.Status404NotFound,
                "invalid_rating" or "invalid_text" => StatusCodes.Status400BadRequest,
                "concurrent_modification" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            };
            await Results.Problem(statusCode: status, title: "No se pudo completar la operación de reseñas",
                extensions: new Dictionary<string, object?> { ["code"] = status == 500 ? "internal_error" : error.Code })
                .ExecuteAsync(context);
        }
    }
}
