using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Playtrough.Api.Models;

namespace Playtrough.Api.Middleware;

public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IOptions<TransportSettings> settings)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = settings.Value.MaxRequestBodySizeBytes;
        if (context.Request.ContentLength > limit)
        {
            await RejectAsync(context);
            return;
        }
        // Enforce unknown-length/chunked bodies while the server reads them.
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
        try { await next(context); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            context.Response.Clear();
            await RejectAsync(context);
        }
    }

    private static Task RejectAsync(HttpContext context) => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Solicitud demasiado grande",
        detail: "El cuerpo de la solicitud supera el tamaño permitido.",
        extensions: new Dictionary<string, object?> { ["code"] = "payload_too_large" }).ExecuteAsync(context);
}
