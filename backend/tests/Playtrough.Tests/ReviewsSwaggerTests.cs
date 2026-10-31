using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Playtrough.Tests;

public class ReviewsSwaggerTests
{
    [Theory]
    [InlineData("get", "200,401,404,429,500", "review_not_found")]
    [InlineData("put", "200,201,400,401,404,409,429,500", "game_not_found")]
    [InlineData("delete", "200,401,404,409,429,500", "review_not_found")]
    public async Task Swagger_DocumentsOwnEndpointsSecurityLocalIdErrorsAndExamples(string method, string statuses, string notFoundCode)
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var root = document.RootElement;
        var operation = root.GetProperty("paths").GetProperty("/api/games/{gameId}/reviews/me").GetProperty(method);
        Assert.Equal("Reviews", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        Assert.True(Assert.Single(operation.GetProperty("security").EnumerateArray()).TryGetProperty("Bearer", out _));
        var id = Assert.Single(operation.GetProperty("parameters").EnumerateArray());
        Assert.Equal("gameId", id.GetProperty("name").GetString());
        Assert.Contains("local", id.GetProperty("description").GetString());
        var responses = operation.GetProperty("responses");
        foreach (var status in statuses.Split(',')) Assert.True(responses.TryGetProperty(status, out _));
        Assert.Equal(notFoundCode, Example(responses, "404").GetProperty("code").GetString());
        Assert.Contains(notFoundCode, responses.GetProperty("404").GetProperty("description").GetString());
        Assert.Equal("rate_limited", Example(responses, "429").GetProperty("code").GetString());
        Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        Assert.False(Example(responses, "200").TryGetProperty("email", out _));
        if (method != "get")
        {
            Assert.Equal("concurrent_modification", Example(responses, "409").GetProperty("code").GetString());
            Assert.Contains("reviews-write", operation.GetProperty("description").GetString());
        }
        if (method == "put")
        {
            var body = operation.GetProperty("requestBody");
            Assert.True(body.GetProperty("required").GetBoolean());
            var media = body.GetProperty("content").GetProperty("application/json");
            Assert.Equal(4.5m, media.GetProperty("examples").GetProperty("withText").GetProperty("value").GetProperty("rating").GetDecimal());
            Assert.False(media.GetProperty("examples").GetProperty("ratingOnly").GetProperty("value").TryGetProperty("text", out _));
            var errors = responses.GetProperty("400").GetProperty("content").GetProperty("application/json").GetProperty("examples");
            foreach (var code in new[] { "invalid_rating", "invalid_text" })
            {
                Assert.Equal(code, errors.GetProperty(code).GetProperty("value").GetProperty("code").GetString());
                Assert.Contains(code, responses.GetProperty("400").GetProperty("description").GetString());
            }
            var success = Example(responses, "201");
            Assert.True(responses.GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _));
            Assert.True(success.TryGetProperty("review", out _));
            Assert.Equal(1, success.GetProperty("ratingSummary").GetProperty("count").GetInt32());
        }
        Assert.Equal(0, context.Provider.DetailCalls);
    }

    [Fact]
    public async Task Swagger_ExposesTypedRequestPrivateResponseFieldsAndSummaryInGameDetail()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("UpsertReviewRequest");
        Assert.Equal("rating", Assert.Single(request.GetProperty("required").EnumerateArray()).GetString());
        var rating = request.GetProperty("properties").GetProperty("rating");
        Assert.Equal("number", rating.GetProperty("type").GetString());
        Assert.Equal(1, rating.GetProperty("minimum").GetInt32());
        Assert.Equal(5, rating.GetProperty("maximum").GetInt32());
        Assert.Equal(0.5m, rating.GetProperty("multipleOf").GetDecimal());
        Assert.Equal(2000, request.GetProperty("properties").GetProperty("text").GetProperty("maxLength").GetInt32());
        Assert.Equal(new[] { "createdAt", "editedAt", "gameId", "id", "rating", "text" },
            schemas.GetProperty("MyReviewDto").GetProperty("properties").EnumerateObject().Select(property => property.Name).Order().ToArray());
        var summary = schemas.GetProperty("RatingSummary").GetProperty("properties");
        Assert.Equal(new[] { "average", "count" }, summary.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.True(schemas.GetProperty("GameDetailsDto").GetProperty("properties").TryGetProperty("ratingSummary", out _));
        var detail = root.GetProperty("paths").GetProperty("/api/games/{externalId}").GetProperty("get");
        Assert.True(Example(detail.GetProperty("responses"), "200").TryGetProperty("ratingSummary", out _));
    }

    [Fact]
    public async Task Swagger_DocumentsPublicListSortsPagingAuthorProjectionAndErrors()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var root = document.RootElement;
        var operation = root.GetProperty("paths").GetProperty("/api/games/{gameId}/reviews").GetProperty("get");
        Assert.Equal("Reviews", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        Assert.True(Assert.Single(operation.GetProperty("security").EnumerateArray()).TryGetProperty("Bearer", out _));
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        var sort = Assert.Single(parameters, parameter => parameter.GetProperty("name").GetString() == "sort").GetProperty("schema");
        Assert.Equal(new[] { "recent", "top", "low" }, sort.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("recent", sort.GetProperty("default").GetString());
        var page = Assert.Single(parameters, parameter => parameter.GetProperty("name").GetString() == "page").GetProperty("schema");
        Assert.Equal(1, page.GetProperty("default").GetInt32());
        Assert.Equal(1, page.GetProperty("minimum").GetInt32());
        var size = Assert.Single(parameters, parameter => parameter.GetProperty("name").GetString() == "pageSize").GetProperty("schema");
        Assert.Equal(10, size.GetProperty("default").GetInt32());
        Assert.Equal(30, size.GetProperty("maximum").GetInt32());
        var responses = operation.GetProperty("responses");
        foreach (var code in new[] { "200", "400", "401", "404", "429", "500" }) Assert.True(responses.TryGetProperty(code, out _));
        Assert.Equal("invalid_query", Example(responses, "400").GetProperty("code").GetString());
        Assert.Equal("game_not_found", Example(responses, "404").GetProperty("code").GetString());
        Assert.Equal("rate_limited", Example(responses, "429").GetProperty("code").GetString());
        Assert.True(responses.GetProperty("429").GetProperty("headers").TryGetProperty("Retry-After", out _));
        var example = Example(responses, "200");
        Assert.Equal(JsonValueKind.Null, example.GetProperty("items")[0].GetProperty("text").ValueKind);
        var author = example.GetProperty("items")[0].GetProperty("author");
        Assert.Equal(new[] { "userId", "username" }, author.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.Equal(new[] { "userId", "username" }, schemas.GetProperty("ReviewAuthorDto").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.True(schemas.GetProperty("ReviewListResponse").GetProperty("properties").TryGetProperty("ratingSummary", out _));
    }

    private static JsonElement Example(JsonElement responses, string status)
        => responses.GetProperty(status).GetProperty("content").GetProperty("application/json").GetProperty("example");
}
