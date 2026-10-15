using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Playtrough.Tests;

public class SwaggerDocumentationTests
{
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
