using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

/// <summary>Request and response examples for the existing role-change endpoint.</summary>
public sealed class AdminRoleExamplesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AdminUsersController) ||
            context.MethodInfo.Name != nameof(AdminUsersController.ChangeRole)) return;

        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var request) == true)
            request.Example = JsonNode.Parse("""{"role":"Moderator"}""");
        if (operation.Responses is not { } responses) return;
        SetExample(responses, "200", """
            {"id":2,"username":"usuario_ejemplo","email":"usuario@example.com","role":"Moderator","emailConfirmed":true}
            """);
        SetExample(responses, "400", """
            {"title":"Role change failed","status":400,"detail":"You cannot change your own role.","code":"cannot_change_own_role"}
            """);
        SetExample(responses, "404", """
            {"title":"Role change failed","status":404,"detail":"The user was not found.","code":"user_not_found"}
            """);
    }

    private static void SetExample(OpenApiResponses responses, string status, string json)
    {
        if (responses.TryGetValue(status, out var response) && response.Content is { } content)
            foreach (var mediaType in content.Values) mediaType.Example = JsonNode.Parse(json);
    }
}
