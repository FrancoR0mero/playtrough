using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Playtrough.Api.Dtos.Reviews;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

public sealed class ReviewsExamplesFilter : IOperationFilter, ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(UpsertReviewRequest) || schema is not OpenApiSchema request) return;
        request.Type = JsonSchemaType.Object;
        request.Required = new HashSet<string> { "rating" };
        request.Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["rating"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Number, Minimum = "1", Maximum = "5", MultipleOf = 0.5m,
                Description = "Obligatorio. Número de 1 a 5 en pasos de 0,5; no admite cadenas numéricas.",
            },
            ["text"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null, MaxLength = 2000,
                Description = "Opcional. Hasta 2000 caracteres Unicode tras el saneamiento; vacío equivale a null. Texto plano sin codificar HTML.",
            },
        };
        request.Example = JsonNode.Parse("""{"rating":4.5,"text":"Opinión ficticia del juego."}""");
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(ReviewsController) || operation.Responses is not { } responses) return;
        var upsert = context.MethodInfo.Name == nameof(ReviewsController.Upsert);
        var delete = context.MethodInfo.Name == nameof(ReviewsController.Delete);
        if (operation.Parameters?.FirstOrDefault(parameter => parameter.Name == "gameId") is OpenApiParameter id)
        {
            id.Description = "ID local de Games; se obtiene del campo id en el detalle, no del externalId.";
            id.Example = JsonValue.Create(7);
        }
        if (upsert && operation.RequestBody is OpenApiRequestBody body) body.Required = true;
        if (upsert && operation.RequestBody?.Content is { } content)
            foreach (var media in content.Values)
            {
                media.Example = null;
                media.Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["withText"] = new OpenApiExample { Summary = "Puntuación y texto ficticio", Value = JsonNode.Parse("""{"rating":4.5,"text":"Opinión ficticia del juego."}""") },
                    ["ratingOnly"] = new OpenApiExample { Summary = "Solo puntuación", Value = JsonNode.Parse("""{"rating":3}""") },
                };
            }

        const string review = """{"id":11,"gameId":7,"rating":4.5,"text":"Opinión ficticia del juego.","createdAt":"2026-10-30T12:00:00Z","editedAt":null}""";
        var success = upsert ? """{"review":REVIEW,"ratingSummary":{"average":4.5,"count":1}}""".Replace("REVIEW", review)
            : delete ? """{"ratingSummary":{"average":null,"count":0}}""" : review;
        SetExample(responses, "200", success);
        SetExample(responses, "201", success);
        if (responses.TryGetValue("201", out var createdResponse) && createdResponse is OpenApiResponse created)
        {
            created.Headers ??= new Dictionary<string, IOpenApiHeader>();
            created.Headers["Location"] = new OpenApiHeader
            {
                Description = "Ruta GET de la reseña propia recién creada.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" },
            };
        }
        SetExample(responses, "404", upsert
            ? """{"title":"No se pudo completar la operación de reseñas","status":404,"code":"game_not_found"}"""
            : """{"title":"No se pudo completar la operación de reseñas","status":404,"code":"review_not_found"}""");
        SetExample(responses, "409", """{"title":"No se pudo completar la operación de reseñas","status":409,"code":"concurrent_modification"}""");
        SetExample(responses, "401", """{"title":"No autenticado","status":401}""");
        SetExample(responses, "500", """{"title":"Error interno","status":500,"code":"internal_error"}""");
        if (responses.TryGetValue("400", out var badRequest) && badRequest.Content is { } errors)
            foreach (var media in errors.Values)
                media.Examples = new Dictionary<string, IOpenApiExample>
                {
                    ["invalid_rating"] = new OpenApiExample
                    {
                        Summary = "Puntuación ausente, no numérica, fuera de rango o sin respetar pasos de 0,5",
                        Value = JsonNode.Parse("""{"title":"No se pudo completar la operación de reseñas","status":400,"code":"invalid_rating"}"""),
                    },
                    ["invalid_text"] = new OpenApiExample
                    {
                        Summary = "Texto de tipo incorrecto o de más de 2000 caracteres después del saneamiento",
                        Value = JsonNode.Parse("""{"title":"No se pudo completar la operación de reseñas","status":400,"code":"invalid_text"}"""),
                    },
                };
    }

    private static void SetExample(OpenApiResponses responses, string status, string json)
    {
        if (responses.TryGetValue(status, out var response) && response.Content is { } content)
            foreach (var media in content.Values) media.Example = JsonNode.Parse(json);
    }
}
