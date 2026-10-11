using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrought.Api.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrought.Api.OpenApi;

/// <summary>
/// Adds realistic request/response examples to the register endpoint in Swagger.
/// Applies only to <see cref="AuthController.Register"/>; Swashbuckle 10 examples are JsonNode based.
/// </summary>
public sealed class RegisterEndpointExamplesFilter : IOperationFilter
{
    private const string JsonContentType = "application/json";

    private const string RequestExample = """
        { "email": "ana@example.com", "username": "ana_dev", "password": "Str0ngPass1" }
        """;

    private const string CreatedExample = """
        { "id": 1, "username": "ana_dev", "email": "ana@example.com", "createdAt": "2026-10-11T12:00:00.000Z" }
        """;

    private const string ValidationProblemExample = """
        { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.", "status": 400, "errors": { "Password": ["Passwords must have at least one uppercase ('A'-'Z')."] } }
        """;

    private const string ConflictExample = """
        { "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Email already registered", "status": 409, "detail": "An account with this email already exists." }
        """;

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AuthController) ||
            context.MethodInfo.Name != nameof(AuthController.Register))
        {
            return;
        }

        if (operation.RequestBody?.Content is { } requestContent &&
            requestContent.TryGetValue(JsonContentType, out var requestMediaType))
        {
            requestMediaType.Example = JsonNode.Parse(RequestExample);
        }

        if (operation.Responses is not { } responses)
        {
            return;
        }

        SetResponseExample(responses, "201", CreatedExample);
        SetResponseExample(responses, "400", ValidationProblemExample);
        SetResponseExample(responses, "409", ConflictExample);
    }

    private static void SetResponseExample(OpenApiResponses responses, string statusCode, string example)
    {
        if (!responses.TryGetValue(statusCode, out var response))
        {
            return;
        }

        if (response.Content is null && response is OpenApiResponse concreteResponse)
        {
            concreteResponse.Content = new Dictionary<string, OpenApiMediaType>();
        }

        if (response.Content is not { } content)
        {
            return;
        }

        if (!content.TryGetValue(JsonContentType, out var mediaType))
        {
            mediaType = new OpenApiMediaType();
            content[JsonContentType] = mediaType;
        }

        mediaType.Example = JsonNode.Parse(example);
    }
}
