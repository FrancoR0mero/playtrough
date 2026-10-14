using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrought.Api.OpenApi;

/// <summary>
/// Restricts documented request and response bodies to <c>application/json</c>.
/// ASP.NET Core output formatters make Swashbuckle list <c>text/plain</c> and <c>text/json</c> as well,
/// which leads Swagger UI to default the response media type dropdown to <c>text/plain</c> and hide the
/// JSON examples. This API is JSON only, so the other media types are pruned from the document.
/// Applied after the generated content exists, so it never removes a body that has no JSON variant.
/// </summary>
public sealed class JsonOnlyContentFilter : IOperationFilter
{
    private const string JsonContentType = "application/json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.RequestBody?.Content is { } requestContent)
        {
            KeepJsonOnly(requestContent);
        }

        if (operation.Responses is not { } responses)
        {
            return;
        }

        foreach (var response in responses.Values)
        {
            if (response.Content is { } responseContent)
            {
                KeepJsonOnly(responseContent);
            }
        }
    }

    private static void KeepJsonOnly(IDictionary<string, OpenApiMediaType> content)
    {
        // Skip when the body has no JSON variant: pruning would leave the media type list empty.
        if (!content.ContainsKey(JsonContentType))
        {
            return;
        }

        foreach (var mediaType in content.Keys.Where(key => key != JsonContentType).ToList())
        {
            content.Remove(mediaType);
        }
    }
}
