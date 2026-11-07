using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

/// <summary>Matches explicit authorization and the API's authenticated-by-default fallback.</summary>
public sealed class AnonymousSecurityFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [];
            return;
        }
        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, context.Document)] = [],
        }];
    }
}
