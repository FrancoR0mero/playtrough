using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

public sealed class GamesExamplesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(GamesController) || operation.Responses is not { } responses) return;
        var search = context.MethodInfo.Name == nameof(GamesController.Search);
        if (search && operation.Parameters?.FirstOrDefault(parameter => parameter.Name == "q") is OpenApiParameter query)
        {
            query.Required = true;
            query.Description = "Texto obligatorio de 2 a 100 caracteres después de recortar espacios.";
            query.Example = JsonValue.Create("juego de ejemplo");
        }
        SetExample(responses, "200", search ? """
            {"items":[{"externalId":42,"name":"Juego de ejemplo","slug":"juego-de-ejemplo","coverUrl":null,"releaseYear":2020,"platforms":["PC de ejemplo"]}],"page":1,"pageSize":20,"hasMore":false}
            """ : """
            {"id":1,"externalId":42,"name":"Juego de ejemplo","slug":"juego-de-ejemplo","summary":"Resumen ficticio en texto plano.","coverUrl":null,"coverUrlLarge":null,"releaseDate":"2020-01-01","platforms":["PC de ejemplo"],"genres":["Aventura de ejemplo"],"developers":["Estudio ficticio"]}
            """);
        SetExample(responses, "400", search ? """
            {"title":"Consulta inválida","status":400,"detail":"Revisá la consulta y los parámetros de paginación.","code":"invalid_query","errors":{"q":["La consulta debe tener entre 2 y 100 caracteres después de recortar espacios."]}}
            """ : """
            {"title":"ID de juego inválido","status":400,"detail":"El ID externo debe ser un entero positivo.","code":"invalid_game_id","errors":{"externalId":["El ID externo debe ser un entero positivo."]}}
            """);
        SetExample(responses, "404", """
            {"title":"Juego no encontrado","status":404,"detail":"No se encontró el juego solicitado.","code":"game_not_found"}
            """);
        SetExample(responses, "503", """
            {"title":"Catálogo no disponible","status":503,"detail":"El catálogo de juegos no está disponible. Intentá nuevamente más tarde.","code":"catalog_unavailable"}
            """);
    }

    private static void SetExample(OpenApiResponses responses, string status, string json)
    {
        if (responses.TryGetValue(status, out var response) && response.Content is { } content)
            foreach (var mediaType in content.Values) mediaType.Example = JsonNode.Parse(json);
    }
}
