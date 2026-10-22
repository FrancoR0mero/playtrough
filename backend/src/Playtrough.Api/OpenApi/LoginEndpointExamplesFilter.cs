using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

/// <summary>Adds login examples and usage rules to Swagger, following the registration filter.</summary>
public sealed class LoginEndpointExamplesFilter : IOperationFilter
{
    private const string JsonContentType = "application/json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AuthController) ||
            context.MethodInfo.Name != nameof(AuthController.Login))
        {
            return;
        }

        operation.Summary = "Iniciar sesión con email o nombre de usuario";
        operation.Description = """
            Ingresá un `identifier` (email o nombre de usuario) y la contraseña de una cuenta existente.
            Ambos campos son obligatorios. El login no aplica la política de complejidad del registro.

            - Si `identifier` contiene `@`, se busca por email; en caso contrario, por nombre de usuario.
            - Usuario inexistente y contraseña incorrecta devuelven el mismo error genérico **401**.
            - Email sin confirmar devuelve **403**, con `extensions.code: email_not_confirmed` (serializado como `code`).
            - El quinto intento fallido bloquea la cuenta durante **15 minutos** y devuelve **423**.
              Mientras dure el bloqueo, tampoco se acepta la contraseña correcta.
            - Un login exitoso resetea el contador de intentos fallidos.
            - Por defecto, el access token dura **15 minutos** y el refresh token **7 días**.
              `expiresAt` indica el vencimiento UTC del access token. El refresh token se guarda hasheado.

            No requiere un JWT previo. Creá primero la cuenta mediante `/api/auth/register`.
            Elegí el ejemplo **Por email** o **Por username** para probar ambos identificadores.
            Los tokens de la respuesta de ejemplo son ilustrativos y no sirven para autenticarse.
            """;

        if (operation.RequestBody?.Content is { } requestContent &&
            requestContent.TryGetValue(JsonContentType, out var requestMediaType))
        {
            requestMediaType.Example = null;
            requestMediaType.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["email"] = new OpenApiExample
                {
                    Summary = "Por email",
                    Value = JsonNode.Parse("""{"identifier":"ana@example.com","password":"Str0ngPass1"}"""),
                },
                ["username"] = new OpenApiExample
                {
                    Summary = "Por username",
                    Value = JsonNode.Parse("""{"identifier":"ana_dev","password":"Str0ngPass1"}"""),
                },
            };
        }

        if (operation.Responses is not { } responses) return;

        SetResponseExample(responses, "200", "Login exitoso: tokens y datos básicos del usuario.", """
            {
              "accessToken": "JWT_DE_EJEMPLO_NO_VALIDO",
              "refreshToken": "REFRESH_TOKEN_DE_EJEMPLO_NO_VALIDO",
              "expiresAt": "2026-10-15T12:15:00Z",
              "user": { "id": 1, "username": "ana_dev", "email": "ana@example.com", "role": "User" }
            }
            """);
        SetResponseExample(responses, "400", "Campos requeridos ausentes o inválidos.", """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": {
                "Identifier": ["Email or username is required."],
                "Password": ["Password is required."]
              }
            }
            """);
        SetResponseExample(responses, "401", "Credenciales inválidas; mismo mensaje para usuario inexistente o contraseña incorrecta.", """
            { "title": "Invalid credentials", "status": 401, "detail": "The email/username or password is incorrect." }
            """);
        SetResponseExample(responses, "403", "Email sin confirmar; extensions.code: email_not_confirmed.", """
            { "title": "Email not confirmed", "status": 403, "detail": "Confirm your email before logging in.", "code": "email_not_confirmed" }
            """);
        SetResponseExample(responses, "423", "Cuenta bloqueada temporalmente por cinco intentos fallidos. Reintentar después de 15 minutos.", """
            { "title": "Account locked", "status": 423, "detail": "This account is temporarily locked after too many failed login attempts. Try again in 15 minutes." }
            """);
        SetResponseExample(responses, "500", "Error interno inesperado; no se exponen detalles sensibles.", """
            { "title": "An unexpected error occurred while logging in.", "status": 500 }
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
