using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Playtrough.Tests;

public class SwaggerDocumentationTests
{
    [Fact]
    public async Task Swagger_PublishesPublicRefreshContractExamplesAndRotationRules()
    {
        // Arrange
        using var factory = new LoginApiFactory();
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
        using var factory = new LoginApiFactory();
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
}
