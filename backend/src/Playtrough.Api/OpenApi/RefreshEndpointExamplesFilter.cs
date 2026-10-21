using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

/// <summary>Documents refresh rotation and examples using the same style as login/register.</summary>
public sealed class RefreshEndpointExamplesFilter : IOperationFilter
{
    private const string JsonContentType = "application/json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AuthController) ||
            context.MethodInfo.Name != nameof(AuthController.Refresh)) return;

        operation.Summary = "Renovar la sesión y rotar el refresh token";
        operation.Description = """
            Endpoint **público**: no requiere el header `Authorization`, porque el access token puede estar vencido.
            Enviá el `refreshToken` vigente obtenido del login o de la última renovación. El campo es obligatorio.

            - **200:** devuelve un access token y un refresh token nuevos, `expiresAt` (UTC) y los datos actuales del usuario.
              Los roles se consultan en la base; no se copian del JWT anterior. Reemplazá **ambos tokens** almacenados.
            - Por defecto, el access token dura **15 minutos** y el nuevo refresh token **7 días**, según `JwtSettings`.
              `expiresAt` corresponde al access token; los refresh tokens se almacenan únicamente como hashes.
            - La rotación revoca el token utilizado y lo vincula al nuevo dentro de una única transacción.
              Si la operación falla antes del commit, se revierte sin consumir el token anterior.
            - **400:** body vacío o `refreshToken` ausente, vacío o compuesto solo por espacios.
            - **401:** token inexistente, vencido, revocado o usuario inexistente, bloqueado o no habilitado.
              Todos estos casos devuelven el **mismo mensaje genérico**.
            - Reutilizar un token ya rotado se considera posible robo: revoca los refresh tokens activos de ese usuario.
              Reutilizar uno revocado por logout también devuelve 401, pero no revoca las otras sesiones.
            - Dos renovaciones simultáneas del mismo token producen un **200** y un **401**. La política estricta de
              reutilización revoca también el refresh token recién emitido por la solicitud ganadora.
              El cliente debe compartir una sola petición entre los llamadores concurrentes.
            - Los JWT de acceso ya emitidos conservan su validez hasta vencer.
            - **500:** error interno inesperado, sin detalles sensibles en la respuesta.

            Para probar: iniciá sesión, pegá el refresh token real en el ejemplo **Renovación** y ejecutalo una vez.
            Guardá el nuevo refresh token de la respuesta para la siguiente renovación.
            Volver a enviar el anterior provoca la detección de reutilización y devuelve 401.
            Los valores de ejemplo son ilustrativos y no sirven para renovar una sesión real.
            """;
        operation.Security = [];

        if (operation.RequestBody is OpenApiRequestBody body) body.Required = true;
        if (operation.RequestBody?.Content is { } content && content.TryGetValue(JsonContentType, out var mediaType))
        {
            mediaType.Example = null;
            mediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["renewal"] = new OpenApiExample
                {
                    Summary = "Renovación: pegar el refresh token vigente",
                    Value = JsonNode.Parse("""{"refreshToken":"PEGAR_REFRESH_TOKEN_ACTUAL_DEL_LOGIN_O_ULTIMO_REFRESH"}"""),
                },
                ["missing"] = new OpenApiExample
                {
                    Summary = "Sin token: respuesta 400",
                    Value = JsonNode.Parse("{}"),
                },
                ["invalid"] = new OpenApiExample
                {
                    Summary = "Token inexistente: respuesta 401",
                    Value = JsonNode.Parse("""{"refreshToken":"TOKEN_INEXISTENTE_DE_EJEMPLO"}"""),
                },
            };
        }

        if (operation.Responses is not { } responses) return;
        SetResponseExample(responses, "200", "Renovación exitosa: guardar ambos tokens y los datos actualizados del usuario.", """
            {
              "accessToken": "JWT_NUEVO_DE_EJEMPLO_NO_VALIDO",
              "refreshToken": "REFRESH_NUEVO_DE_EJEMPLO_NO_VALIDO",
              "expiresAt": "2026-10-16T12:15:00Z",
              "user": { "id": 1, "username": "ana_dev", "email": "ana@example.com", "role": "User" }
            }
            """);
        SetResponseExample(responses, "400", "Body vacío o refreshToken requerido ausente o vacío.", """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": { "RefreshToken": ["Refresh token is required."] }
            }
            """);
        SetResponseExample(responses, "401", "Token inválido o usuario no habilitado: mismo mensaje genérico en todos los casos.", """
            { "title": "Invalid refresh token", "status": 401, "detail": "The refresh token is invalid or cannot be used." }
            """);
        SetResponseExample(responses, "500", "Error interno inesperado; no se exponen detalles sensibles.", """
            { "title": "An unexpected error occurred while refreshing the session.", "status": 500 }
            """);
    }

    private static void SetResponseExample(OpenApiResponses responses, string statusCode, string description, string example)
    {
        if (!responses.TryGetValue(statusCode, out var response)) return;
        if (response is OpenApiResponse concreteResponse)
        {
            concreteResponse.Description = description;
            concreteResponse.Content ??= new Dictionary<string, OpenApiMediaType>();
        }
        if (response.Content is not { } content) return;
        if (!content.TryGetValue(JsonContentType, out var mediaType))
        {
            mediaType = new OpenApiMediaType();
            content[JsonContentType] = mediaType;
        }
        mediaType.Example = JsonNode.Parse(example);
    }
}
