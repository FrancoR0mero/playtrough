using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Controllers;
using Playtrough.Api.Dtos.Friends;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

public sealed class FriendsExamplesFilter : ISchemaFilter, IOperationFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(SendFriendRequest) || schema is not OpenApiSchema request) return;
        request.Type = JsonSchemaType.Object;
        request.Required = new HashSet<string> { "username" };
        request.Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["username"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Username del destinatario, sin distinguir mayúsculas." },
        };
    }
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(FriendsController)) return;
        if (operation.RequestBody?.Content is { } content)
            foreach (var media in content.Values) media.Example = JsonNode.Parse("""{"username":"gamer_amigo"}""");
        if (operation.Parameters is { } parameters)
            foreach (var parameter in parameters.OfType<OpenApiParameter>())
                if (parameter.Name is "page" or "pageSize" && parameter.Schema is OpenApiSchema paging)
                {
                    paging.Default = JsonValue.Create(parameter.Name == "page" ? 1 : 20);
                    paging.Minimum = "1";
                    if (parameter.Name == "pageSize") paging.Maximum = "50";
                }
    }
}
