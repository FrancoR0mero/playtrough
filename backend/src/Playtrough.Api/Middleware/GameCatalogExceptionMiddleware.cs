using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Middleware;

// Runs inside the existing exception handler/developer page. Known catalog errors
// keep their public JSON contract in every environment; unrelated errors keep theirs.
public sealed class GameCatalogExceptionMiddleware(RequestDelegate next, ILogger<GameCatalogExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (GameCatalogUnavailableException) when (!context.Response.HasStarted)
        {
            logger.LogWarning("Game catalog unavailable. TraceId: {TraceId}.", context.TraceIdentifier);
            await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Catálogo no disponible",
                detail: "El catálogo de juegos no está disponible. Intentá nuevamente más tarde.",
                extensions: new Dictionary<string, object?> { ["code"] = "catalog_unavailable" }).ExecuteAsync(context);
        }
    }
}
