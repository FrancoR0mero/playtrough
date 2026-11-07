using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Playtrough.Tests;

public class GamesSwaggerTests
{
    [Theory]
    [InlineData("/api/games/search", "invalid_query", false)]
    [InlineData("/api/games/{externalId}", "invalid_game_id", true)]
    public async Task Swagger_DocumentsGameContractsSecurityErrorsAndExamples(string path, string badRequestCode, bool detail)
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");
        Assert.Equal("Games", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        Assert.True(Assert.Single(operation.GetProperty("security").EnumerateArray()).TryGetProperty("Bearer", out _));
        var responses = operation.GetProperty("responses");
        foreach (var code in new[] { "200", "400", "401", "429", "500", "503" }) Assert.True(responses.TryGetProperty(code, out _));
        Assert.Equal(badRequestCode, Example(responses, "400").GetProperty("code").GetString());
        Assert.Equal("catalog_unavailable", Example(responses, "503").GetProperty("code").GetString());
        Assert.Equal("rate_limited", Example(responses, "429").GetProperty("code").GetString());
        Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        Assert.Contains(badRequestCode, responses.GetProperty("400").GetProperty("description").GetString());
        Assert.Contains("catalog_unavailable", responses.GetProperty("503").GetProperty("description").GetString());
        var success = Example(responses, "200");
        if (detail)
        {
            Assert.Equal("game_not_found", Example(responses, "404").GetProperty("code").GetString());
            Assert.Contains("game_not_found", responses.GetProperty("404").GetProperty("description").GetString());
            Assert.True(success.TryGetProperty("id", out _));
            Assert.True(success.TryGetProperty("coverUrlLarge", out _));
            Assert.True(success.TryGetProperty("summary", out _));
        }
        else
        {
            var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
            var query = Assert.Single(parameters, item => item.GetProperty("name").GetString() == "q");
            Assert.True(query.GetProperty("required").GetBoolean());
            Assert.Contains("recortar", query.GetProperty("description").GetString());
            var pageSize = Assert.Single(parameters, item => item.GetProperty("name").GetString() == "pageSize");
            Assert.Equal(40, pageSize.GetProperty("schema").GetProperty("maximum").GetInt32());
            Assert.True(success.TryGetProperty("hasMore", out _));
            Assert.True(success.GetProperty("items")[0].TryGetProperty("releaseYear", out _));
        }
        Assert.Equal(0, context.Provider.SearchCalls + context.Provider.DetailCalls);
        Assert.Empty(await context.ReadGamesAsync());
    }

    private static JsonElement Example(JsonElement responses, string status)
        => responses.GetProperty(status).GetProperty("content").GetProperty("application/json").GetProperty("example");
}
