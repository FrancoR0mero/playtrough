using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

/// <summary>Documents shared API errors and the public documentation's tag descriptions.</summary>
public sealed class ApiDocumentationFilter : IOperationFilter, IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
        => document.Tags = new HashSet<OpenApiTag>
        {
            new OpenApiTag { Name = "Auth", Description = "Registro, sesión, recuperación y confirmación de email." },
            new OpenApiTag { Name = "Admin", Description = "Usuarios, roles y auditoría. Requiere rol Admin." },
            new OpenApiTag { Name = "Games", Description = "Búsqueda y fichas de juegos. Requiere autenticación; límite compartido por usuario." },
        };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is not { } responses) return;

        if (responses.TryGetValue("429", out var rateLimited) && rateLimited is OpenApiResponse rejection)
        {
            rejection.Description = "Demasiadas solicitudes; extensions.code: rate_limited. " +
                "Retry-After indica los segundos que se deben esperar antes de reintentar.";
            rejection.Headers ??= new Dictionary<string, IOpenApiHeader>();
            rejection.Headers["Retry-After"] = new OpenApiHeader
            {
                Description = "Segundos de espera, redondeados hacia arriba (mínimo 1).",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
            };
            if (rejection.Content is { } content)
                foreach (var mediaType in content.Values)
                    mediaType.Example = JsonNode.Parse("""
                        {"title":"Demasiadas solicitudes","status":429,"detail":"Se alcanzó el límite de solicitudes. Intentá nuevamente más tarde.","code":"rate_limited"}
                        """);
        }

        if (responses.TryGetValue("413", out var tooLarge) && tooLarge is OpenApiResponse bodyLimit)
            bodyLimit.Description = "El cuerpo supera el máximo configurado; extensions.code: payload_too_large.";

        if (responses.TryGetValue("401", out var unauthorized) && unauthorized is OpenApiResponse auth &&
            auth.Description is null or "Unauthorized")
            auth.Description = "Se requiere un access token válido en Authorization: Bearer.";

        if (responses.TryGetValue("403", out var forbidden) && forbidden is OpenApiResponse role &&
            role.Description is null or "Forbidden")
            role.Description = "El usuario autenticado no tiene el rol Admin requerido.";

        if (responses.TryGetValue("500", out var internalError) && internalError is OpenApiResponse error &&
            error.Description?.Contains("internal_error", StringComparison.Ordinal) != true)
            error.Description = (error.Description is null or "Internal Server Error" ? "Error interno sin detalles sensibles." : error.Description) +
                " Las excepciones no controladas usan extensions.code: internal_error.";
    }
}
