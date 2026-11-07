using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Playtrough.Api.Dtos.Admin;
using Playtrough.Api.Dtos.Auth;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Playtrough.Api.OpenApi;

/// <summary>Illustrative model examples only; example credentials and tokens are not usable.</summary>
public sealed class DtoExamplesSchemaFilter : ISchemaFilter
{
    private static readonly IReadOnlyDictionary<Type, string> Examples = new Dictionary<Type, string>
    {
        [typeof(RegisterRequestDto)] = """
            {"email":"ana@example.com","username":"ana_dev","password":"Str0ngPass1"}
            """,
        [typeof(RegisterResponseDto)] = """
            {"id":1,"username":"ana_dev","email":"ana@example.com","createdAt":"2026-10-21T12:00:00Z","emailConfirmationRequired":true}
            """,
        [typeof(LoginRequestDto)] = """
            {"identifier":"ana@example.com","password":"Str0ngPass1"}
            """,
        [typeof(LoginResponseDto)] = """
            {"accessToken":"JWT_DE_EJEMPLO_NO_VALIDO","refreshToken":"REFRESH_TOKEN_DE_EJEMPLO_NO_VALIDO","expiresAt":"2026-10-21T12:15:00Z","user":{"id":1,"username":"ana_dev","email":"ana@example.com","role":"User"}}
            """,
        [typeof(RefreshRequestDto)] = """
            {"refreshToken":"PEGAR_REFRESH_TOKEN_ACTUAL_DEL_LOGIN_O_ULTIMO_REFRESH"}
            """,
        [typeof(RefreshResponseDto)] = """
            {"accessToken":"JWT_NUEVO_DE_EJEMPLO_NO_VALIDO","refreshToken":"REFRESH_NUEVO_DE_EJEMPLO_NO_VALIDO","expiresAt":"2026-10-21T12:15:00Z","user":{"id":1,"username":"ana_dev","email":"ana@example.com","role":"User"}}
            """,
        [typeof(ChangeRoleRequest)] = """{"role":"Moderator"}""",
        [typeof(AdminUserDto)] = """
            {"id":2,"username":"usuario_ejemplo","email":"usuario@example.com","role":"Moderator","emailConfirmed":true}
            """,
    };

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is OpenApiSchema concrete && Examples.TryGetValue(context.Type, out var example))
            concrete.Example = JsonNode.Parse(example);
    }
}
