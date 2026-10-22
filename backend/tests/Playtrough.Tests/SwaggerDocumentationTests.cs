using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Playtrough.Api.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace Playtrough.Tests;

public class SwaggerDocumentationTests
{
    [Fact]
    public async Task Swagger_EnablesHttpBearerOnlyForProtectedOperations()
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;

        // Assert the scheme consumed by Swagger UI, including automatic Bearer prefix handling.
        var root = document.RootElement;
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
        Assert.True(!root.TryGetProperty("security", out var global) || global.GetArrayLength() == 0);
        var paths = root.GetProperty("paths");
        foreach (var (path, method) in new[]
        {
            ("/api/Auth/logout", "post"), ("/api/admin/users", "get"),
            ("/api/admin/users/{id}/role", "put"), ("/api/admin/role-changes", "get"),
        })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            var requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());
            Assert.Empty(requirement.GetProperty("Bearer").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    [InlineData("confirm-email")]
    [InlineData("resend-confirmation")]
    public async Task Swagger_PublicAuthOperations_HaveNoBearerRequirement(string endpoint)
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;

        // Public operations remain usable from Swagger UI without Authorize.
        var operation = document.RootElement.GetProperty("paths").GetProperty($"/api/Auth/{endpoint}").GetProperty("post");
        Assert.Empty(operation.GetProperty("security").EnumerateArray());
    }

    [Fact]
    public async Task Swagger_PublishesPublicRefreshContractExamplesAndRotationRules()
    {
        // Arrange
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json");

        // Assert the actual generated document, without replacing the Swagger pipeline.
        Assert.NotNull(document);
        var refresh = document.RootElement.GetProperty("paths").GetProperty("/api/Auth/refresh").GetProperty("post");
        Assert.Contains("Renovar la sesión", refresh.GetProperty("summary").GetString());
        var description = refresh.GetProperty("description").GetString();
        Assert.NotNull(description);
        Assert.Contains("público", description);
        Assert.Contains("15 minutos", description);
        Assert.Contains("7 días", description);
        Assert.Contains("única transacción", description);
        Assert.Contains("reutilización", description);
        Assert.Contains("solicitud ganadora", description);
        Assert.Contains("logout", description);
        Assert.True(!refresh.TryGetProperty("security", out var security) || security.GetArrayLength() == 0);

        var requestBody = refresh.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        var requestContent = requestBody.GetProperty("content");
        Assert.Single(requestContent.EnumerateObject());
        var examples = requestContent.GetProperty("application/json").GetProperty("examples");
        Assert.Equal("PEGAR_REFRESH_TOKEN_ACTUAL_DEL_LOGIN_O_ULTIMO_REFRESH",
            examples.GetProperty("renewal").GetProperty("value").GetProperty("refreshToken").GetString());
        Assert.Empty(examples.GetProperty("missing").GetProperty("value").EnumerateObject());
        Assert.Equal("TOKEN_INEXISTENTE_DE_EJEMPLO",
            examples.GetProperty("invalid").GetProperty("value").GetProperty("refreshToken").GetString());

        var responses = refresh.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "500" })
        {
            var responseContent = responses.GetProperty(status).GetProperty("content");
            Assert.Single(responseContent.EnumerateObject());
            var json = responseContent.GetProperty("application/json");
            var example = json.GetProperty("example");
            if (status == "200")
            {
                Assert.EndsWith("/RefreshResponseDto", json.GetProperty("schema").GetProperty("$ref").GetString());
                Assert.Equal("JWT_NUEVO_DE_EJEMPLO_NO_VALIDO", example.GetProperty("accessToken").GetString());
                Assert.Equal("REFRESH_NUEVO_DE_EJEMPLO_NO_VALIDO", example.GetProperty("refreshToken").GetString());
                Assert.True(example.TryGetProperty("expiresAt", out _));
                Assert.Equal("User", example.GetProperty("user").GetProperty("role").GetString());
            }
            else
            {
                Assert.Equal(int.Parse(status), example.GetProperty("status").GetInt32());
                if (status == "400") Assert.True(example.GetProperty("errors").TryGetProperty("RefreshToken", out _));
                if (status == "401") Assert.Equal("The refresh token is invalid or cannot be used.", example.GetProperty("detail").GetString());
            }
        }
    }

    [Fact]
    public async Task Swagger_PublishesLoginExamplesAndPreservesRegistrationExamples()
    {
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();
        using var document = await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json");
        Assert.NotNull(document);
        var paths = document.RootElement.GetProperty("paths");
        var login = paths.GetProperty("/api/Auth/login").GetProperty("post");
        Assert.Contains("Iniciar sesión", login.GetProperty("summary").GetString());
        Assert.Contains("15 minutos", login.GetProperty("description").GetString());

        var content = login.GetProperty("requestBody").GetProperty("content");
        Assert.Single(content.EnumerateObject());
        var examples = content.GetProperty("application/json").GetProperty("examples");
        Assert.Equal("ana@example.com", examples.GetProperty("email").GetProperty("value").GetProperty("identifier").GetString());
        Assert.Equal("ana_dev", examples.GetProperty("username").GetProperty("value").GetProperty("identifier").GetString());

        var responses = login.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "423", "500" })
        {
            var responseContent = responses.GetProperty(status).GetProperty("content");
            Assert.Single(responseContent.EnumerateObject());
            var example = responseContent.GetProperty("application/json").GetProperty("example");
            if (status == "200")
            {
                Assert.True(example.TryGetProperty("accessToken", out _));
                Assert.True(example.TryGetProperty("refreshToken", out _));
                Assert.True(example.TryGetProperty("expiresAt", out _));
                Assert.Equal("ana_dev", example.GetProperty("user").GetProperty("username").GetString());
            }
            else Assert.Equal(int.Parse(status), example.GetProperty("status").GetInt32());
        }

        var register = paths.GetProperty("/api/Auth/register").GetProperty("post");
        Assert.Equal("ana_dev", register.GetProperty("responses").GetProperty("201")
            .GetProperty("content").GetProperty("application/json").GetProperty("example")
            .GetProperty("username").GetString());
    }

    [Fact]
    public async Task Development_PublishesMetadataTagsAndSummaries_WithoutTestEndpoints()
    {
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
        var root = document.RootElement;
        var info = root.GetProperty("info");
        Assert.Equal("Playtrough API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());
        Assert.Contains("gamers", info.GetProperty("description").GetString());
        Assert.Equal(new[] { "Admin", "Auth" }, root.GetProperty("tags").EnumerateArray()
            .Select(tag => tag.GetProperty("name").GetString()).Order().ToArray());

        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            Assert.False(path.Name.StartsWith("/api/test", StringComparison.OrdinalIgnoreCase));
            Assert.False(path.Name.StartsWith("/__tests", StringComparison.OrdinalIgnoreCase));
            foreach (var operation in path.Value.EnumerateObject())
            {
                Assert.False(string.IsNullOrWhiteSpace(operation.Value.GetProperty("summary").GetString()));
                Assert.Equal(path.Name.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Auth",
                    Assert.Single(operation.Value.GetProperty("tags").EnumerateArray()).GetString());
            }
        }
        using var ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("<title>Playtrough API</title>", await ui.Content.ReadAsStringAsync());
        using var configuration = await client.GetAsync("/swagger/index.js");
        Assert.Equal(HttpStatusCode.OK, configuration.StatusCode);
        Assert.Contains("v1/swagger.json", await configuration.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Development", true, HttpStatusCode.OK)]
    [InlineData("Development", false, HttpStatusCode.NotFound)]
    [InlineData("Production", false, HttpStatusCode.NotFound)]
    [InlineData("Production", true, HttpStatusCode.NotFound)]
    [InlineData("Staging", false, HttpStatusCode.NotFound)]
    [InlineData("Staging", true, HttpStatusCode.NotFound)]
    [InlineData("Testing", false, HttpStatusCode.NotFound)]
    [InlineData("Testing", true, HttpStatusCode.NotFound)]
    public async Task Swagger_RequiresDevelopmentAndEnabled_ForUiAndDocument(string environment, bool enabled, HttpStatusCode expected)
    {
        using var factory = new LoginApiFactory(environment: environment);
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = enabled.ToString() })));
        using var client = configured.CreateClient();
        foreach (var path in new[] { "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json", "/swagger/swagger-ui-bundle.js" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.NotFound) TransportEndpointsTests.AssertHeaders(response);
            else Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }
    }

    [Fact]
    public async Task Production_DefaultConfiguration_DoesNotExposeSwagger()
    {
        using var factory = new LoginApiFactory(environment: Environments.Production);
        using var client = factory.CreateClient();
        using var ui = await client.GetAsync("/swagger/index.html");
        using var document = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, ui.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
    }

    [Fact]
    public void Startup_RejectsInvalidSwaggerSwitch_WithoutEchoingItsValue()
    {
        using var factory = new LoginApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "test-only-invalid-canary" })));
        var error = Assert.Throws<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("Swagger", error.Message);
        Assert.DoesNotContain("canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void SecurityFilter_ProtectsEndpointsCoveredByTheFallbackPolicy()
    {
        using var factory = new LoginApiFactory(environment: Environments.Development);
        var descriptions = factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var description = descriptions.ApiDescriptionGroups.Items.SelectMany(group => group.Items)
            .Single(api => api.RelativePath == "__tests/fallback");
        var operation = new OpenApiOperation();
        var context = new OperationFilterContext(description, factory.Services.GetRequiredService<ISchemaGenerator>(),
            new SchemaRepository("v1"), new OpenApiDocument(), typeof(FallbackTestController).GetMethod(nameof(FallbackTestController.Get))!);
        new AnonymousSecurityFilter().Apply(operation, context);
        var requirement = Assert.Single(operation.Security!);
        Assert.Equal("Bearer", Assert.Single(requirement).Key.Reference.Id);
    }

    [Theory]
    [InlineData("/api/Auth/register", "post", "201,400,409,413,429,500")]
    [InlineData("/api/Auth/login", "post", "200,400,401,403,413,423,429,500")]
    [InlineData("/api/Auth/refresh", "post", "200,400,401,413,429,500")]
    [InlineData("/api/Auth/logout", "post", "204,400,401,413,429,500")]
    [InlineData("/api/Auth/forgot-password", "post", "200,400,413,429,500")]
    [InlineData("/api/Auth/resend-confirmation", "post", "200,400,413,429,500")]
    [InlineData("/api/Auth/reset-password", "post", "200,400,413,429,500")]
    [InlineData("/api/Auth/confirm-email", "post", "200,400,413,429,500")]
    [InlineData("/api/admin/users", "get", "200,400,401,403,429,500")]
    [InlineData("/api/admin/role-changes", "get", "200,400,401,403,429,500")]
    [InlineData("/api/admin/users/{id}/role", "put", "200,400,401,403,404,413,429,500")]
    public async Task Swagger_DocumentsTypedResponsesAndRateLimitRetryHeader(string path, string method, string statuses)
    {
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var responses = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");
        foreach (var status in statuses.Split(','))
        {
            var response = responses.GetProperty(status);
            Assert.False(string.IsNullOrWhiteSpace(response.GetProperty("description").GetString()));
            if (int.Parse(status) < 400) continue;
            var schema = response.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
            Assert.True(schema?.EndsWith("/ProblemDetails", StringComparison.Ordinal) == true ||
                schema?.EndsWith("/ValidationProblemDetails", StringComparison.Ordinal) == true);
        }
        var rejection = responses.GetProperty("429");
        Assert.Contains("rate_limited", rejection.GetProperty("description").GetString());
        Assert.True(rejection.GetProperty("headers").TryGetProperty("Retry-After", out _));
        Assert.Equal("rate_limited", rejection.GetProperty("content").GetProperty("application/json")
            .GetProperty("example").GetProperty("code").GetString());
        if (responses.TryGetProperty("204", out var empty)) Assert.False(empty.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Swagger_DocumentsActualBusinessCodesAndRoleChangeExamples()
    {
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = document.RootElement.GetProperty("paths");
        var email = paths.GetProperty("/api/Auth/login").GetProperty("post").GetProperty("responses").GetProperty("403");
        Assert.Contains("email_not_confirmed", email.GetProperty("description").GetString());
        Assert.Equal("email_not_confirmed", email.GetProperty("content").GetProperty("application/json")
            .GetProperty("example").GetProperty("code").GetString());
        var role = paths.GetProperty("/api/admin/users/{id}/role").GetProperty("put");
        var roleResponses = role.GetProperty("responses");
        foreach (var code in new[] { "invalid_role", "cannot_change_own_role", "last_admin", "invalid_role_state" })
            Assert.Contains(code, roleResponses.GetProperty("400").GetProperty("description").GetString());
        Assert.Contains("user_not_found", roleResponses.GetProperty("404").GetProperty("description").GetString());
        Assert.Contains("role_change_failed", roleResponses.GetProperty("500").GetProperty("description").GetString());
        Assert.Equal("Moderator", role.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("example").GetProperty("role").GetString());
        Assert.Equal("Moderator", roleResponses.GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("example").GetProperty("role").GetString());
    }

    [Theory]
    [InlineData("RegisterRequestDto", "email", "ana@example.com")]
    [InlineData("RegisterResponseDto", "username", "ana_dev")]
    [InlineData("LoginRequestDto", "identifier", "ana@example.com")]
    [InlineData("LoginResponseDto", "accessToken", "JWT_DE_EJEMPLO_NO_VALIDO")]
    [InlineData("RefreshRequestDto", "refreshToken", "PEGAR_REFRESH_TOKEN_ACTUAL_DEL_LOGIN_O_ULTIMO_REFRESH")]
    [InlineData("RefreshResponseDto", "accessToken", "JWT_NUEVO_DE_EJEMPLO_NO_VALIDO")]
    [InlineData("ChangeRoleRequest", "role", "Moderator")]
    [InlineData("AdminUserDto", "role", "Moderator")]
    public async Task Swagger_PublishesIllustrativeDtoExamples(string schema, string field, string value)
    {
        using var factory = new LoginApiFactory(environment: Environments.Development);
        using var client = factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var example = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(schema).GetProperty("example");
        Assert.Equal(value, example.GetProperty(field).GetString());
    }
}
