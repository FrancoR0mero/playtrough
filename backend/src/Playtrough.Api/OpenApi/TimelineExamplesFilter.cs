using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

public sealed class TimelineExamplesFilter(IOptions<TimelineSettings> settings, TimeProvider time) : IOperationFilter, ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema body) return;
        if (context.Type == typeof(SetReactionRequest))
        {
            body.Type = JsonSchemaType.Object;
            body.Required = new HashSet<string> { "value" };
            body.Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["value"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("like")!, JsonValue.Create("dislike")!],
                },
            };
            body.Example = JsonNode.Parse("""{"value":"like"}""");
            return;
        }
        if (context.Type != typeof(UpdateTimelineVisibilityRequest)) return;
        body.Type = JsonSchemaType.Object;
        body.Required = new HashSet<string> { "hidden" };
        body.Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["hidden"] = new OpenApiSchema { Type = JsonSchemaType.Boolean, Description = "true oculta; false vuelve a mostrar." },
        };
        body.Example = JsonNode.Parse("""{"hidden":true}""");
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType == typeof(TimelineVisibilityController))
        {
            if (operation.RequestBody is OpenApiRequestBody body) body.Required = true;
            if (operation.Responses?.TryGetValue("400", out var badBody) == true && badBody is OpenApiResponse invalidBody)
                invalidBody.Description = "invalid_body: cuerpo ausente, JSON inválido o hidden no booleano.";
            if (operation.Responses?.TryGetValue("404", out var missingEntry) == true && missingEntry is OpenApiResponse notFoundEntry)
                notFoundEntry.Description = "entry_not_found: entrada inexistente o ajena. No revela si otro usuario es su dueño.";
            return;
        }
        if (context.MethodInfo.DeclaringType != typeof(TimelineController) && context.MethodInfo.DeclaringType != typeof(FeedController)) return;
        if (operation.Parameters is { } parameters)
            foreach (var parameter in parameters.OfType<OpenApiParameter>())
            {
                switch (parameter.Name?.ToLowerInvariant())
                {
                    case "limit":
                        parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1",
                            Maximum = settings.Value.MaxLimit.ToString(CultureInfo.InvariantCulture), Default = JsonValue.Create(settings.Value.DefaultLimit) };
                        break;
                    case "year":
                        parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1970",
                            Maximum = Math.Min(9999, time.GetUtcNow().Year + 1).ToString(CultureInfo.InvariantCulture) };
                        break;
                    case "month":
                        parameter.Description = "1–12. Sin year, incluye el mes en todos los años.";
                        parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "12" };
                        break;
                    case "type":
                        parameter.Description = "Uno o varios tipos separados por coma: added, status_changed, reviewed.";
                        parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String };
                        break;
                    case "cursor":
                        parameter.Description = "Cursor opaco v1 recibido en nextCursor, protegido contra cambios y vinculado a usuario y filtros. Omitir en la primera página.";
                        parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 4096 };
                        break;
                }
            }
        if (operation.Responses is not { } responses) return;
        if (responses.TryGetValue("400", out var bad) && bad is OpenApiResponse invalid)
            invalid.Description = "invalid_filter: filtros o límite inválidos. invalid_cursor: cursor inválido, modificado o de otra consulta/versión.";
        if (responses.TryGetValue("403", out var denied) && denied is OpenApiResponse restricted)
            restricted.Description = "history_restricted: cuenta privada sin amistad; extensions.restriction=private.";
        if (responses.TryGetValue("404", out var missing) && missing is OpenApiResponse notFound)
            notFound.Description = "user_not_found: usuario inexistente, sin confirmar o bloqueado (excepto dueño).";
    }
}
