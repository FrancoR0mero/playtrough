using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Playtrough.Api.Dtos.Users;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

public sealed class UsersExamplesFilter : ISchemaFilter, IOperationFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(UpdateProfileRequest) || schema is not OpenApiSchema request) return;
        request.Type = JsonSchemaType.Object;
        request.Required = new HashSet<string>();
        request.Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["username"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 3, MaxLength = 20,
                Pattern = "^[a-zA-Z0-9_]{3,20}$", Description = "Opcional. Omitir conserva el username actual." },
            ["bio"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, MaxLength = 300,
                Description = "Opcional. Omitir conserva la bio; null o texto vacío la elimina. Límite tras saneamiento." },
        };
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(UsersController) || operation.Responses is not { } responses) return;
        if (operation.RequestBody?.Content is { } content)
            foreach (var media in content.Values)
                media.Example = JsonNode.Parse("""{"username":"gamer_nuevo","bio":"Me gustan los RPG 🎮"}""");
        if (responses.TryGetValue("429", out var response) && response is OpenApiResponse limited)
        {
            limited.Description = "rate_limited: demasiadas solicitudes.";
            limited.Headers ??= new Dictionary<string, IOpenApiHeader>();
            limited.Headers["Retry-After"] = new OpenApiHeader
            {
                Description = "Segundos hasta poder reintentar.", Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
            };
        }
    }
}
