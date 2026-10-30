using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class LibraryEndpointsTests
{
    private static readonly string[] States = ["playing", "backlog", "beaten", "completed", "abandoned"];

    [Fact]
    public async Task Abandoned_CanBeAddedResumedFilteredAndRemoved()
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var id = await OpenGame(client);
        Assert.Equal("abandoned", (await Add(client, id, "abandoned")).Status);
        var list = (await client.GetFromJsonAsync<LibraryListResponseDto>("/api/library?status=abandoned"))!;
        Assert.Single(list.Items);
        Assert.Equal(1, list.Counts.Abandoned);
        Assert.Equal(1, list.Counts.Total);
        using var resumed = await client.PutAsJsonAsync($"/api/library/{id}/status", new { status = "playing" });
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        using var abandoned = await client.PutAsJsonAsync($"/api/library/{id}/status", new { status = "abandoned" });
        Assert.Equal(HttpStatusCode.OK, abandoned.StatusCode);
        Assert.Equal("abandoned", (await client.GetFromJsonAsync<LibraryEntryDto>($"/api/library/games/{id}"))!.Status);
        using var removed = await client.DeleteAsync($"/api/library/{id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        list = (await client.GetFromJsonAsync<LibraryListResponseDto>("/api/library"))!;
        Assert.Equal(0, list.Counts.Abandoned);
        Assert.Equal(0, list.Counts.Total);
    }

    private static async Task<int> OpenGame(HttpClient client, int externalId = 5)
    {
        using var response = (await client.GetFromJsonAsync<JsonDocument>($"/api/games/{externalId}"))!;
        return response.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<LibraryEntryDto> Add(HttpClient client, int gameId, string status, string? date = null)
    {
        using var response = await client.PostAsJsonAsync("/api/library", new { gameId, status, occurredOn = date });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/library/games/{gameId}", response.Headers.Location!.ToString());
        return (await response.Content.ReadFromJsonAsync<LibraryEntryDto>())!;
    }

    [Theory]
    [InlineData("GET", "/api/library")]
    [InlineData("GET", "/api/library/games/1")]
    [InlineData("POST", "/api/library")]
    [InlineData("PUT", "/api/library/1/status")]
    [InlineData("DELETE", "/api/library/1")]
    public async Task EveryEndpoint_RequiresAuthentication(string method, string path)
    {
        using var context = new GamesApiTestContext();
        using var client = context.Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { gameId = 1, status = "playing" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Moderator")]
    [InlineData("Admin")]
    public async Task FullHttpFlow_UsesRealLoginCatalogPersistenceDatesCountsAndRemoval(string role)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync(role);
        using var search = (await client.GetFromJsonAsync<JsonDocument>("/api/games/search?q=Portal"))!;
        Assert.Equal(2, search.RootElement.GetProperty("items").GetArrayLength());
        var id = await OpenGame(client);
        using var missing = await client.GetAsync($"/api/library/games/{id}");
        await GamesEndpointsTests.AssertProblemAsync(missing, HttpStatusCode.NotFound, "not_in_library");
        var added = await Add(client, id, "backlog", "2026-01-02");
        Assert.Equal("Portal 2", added.Name);
        Assert.Equal(5, added.ExternalId);
        Assert.Equal(2004, added.ReleaseYear);
        Assert.Equal(new DateOnly(2026, 1, 2), added.StatusChangedOn);
        Assert.Equal(DateTimeKind.Utc, added.AddedAt.Kind);
        using var duplicate = await client.PostAsJsonAsync("/api/library", new { gameId = id, status = "backlog" });
        await GamesEndpointsTests.AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "already_in_library");

        using var changed = await client.PutAsJsonAsync($"/api/library/{id}/status", new { status = "playing", occurredOn = "2026-01-05" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = (await changed.Content.ReadFromJsonAsync<LibraryEntryDto>())!;
        Assert.Equal("playing", updated.Status);
        Assert.Equal(new DateOnly(2026, 1, 5), updated.StatusChangedOn);
        using var invalidDate = await client.PutAsJsonAsync($"/api/library/{id}/status", new { status = "beaten", occurredOn = "2026-01-04" });
        await GamesEndpointsTests.AssertProblemAsync(invalidDate, HttpStatusCode.BadRequest, "date_before_previous_event");
        using var problem = JsonDocument.Parse(await invalidDate.Content.ReadAsStringAsync());
        Assert.Equal("2026-01-05", problem.RootElement.GetProperty("minDate").GetString());
        using var repeated = await client.PutAsJsonAsync($"/api/library/{id}/status", new { status = "playing", occurredOn = "2025-01-01" });
        Assert.Equal(updated, await repeated.Content.ReadFromJsonAsync<LibraryEntryDto>());
        using var today = await client.PutAsJsonAsync($"/api/library/{id}/status", new { status = "completed" });
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), (await today.Content.ReadFromJsonAsync<LibraryEntryDto>())!.StatusChangedOn);
        var list = (await client.GetFromJsonAsync<LibraryListResponseDto>("/api/library?status=completed&q=PORTAL&sort=name"))!;
        Assert.Single(list.Items);
        Assert.Equal(new LibraryCountsDto(0, 0, 0, 1, 0), list.Counts);
        using var read = await client.GetAsync($"/api/library/games/{id}");
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "addedAt", "coverUrl", "externalId", "gameId", "name", "releaseYear", "slug", "status", "statusChangedOn" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.True(read.Headers.CacheControl!.NoStore);
        using var removed = await client.DeleteAsync($"/api/library/{id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await removed.Content.ReadAsStringAsync());
        using var removeAgain = await client.DeleteAsync($"/api/library/{id}");
        await GamesEndpointsTests.AssertProblemAsync(removeAgain, HttpStatusCode.NotFound, "not_in_library");
        var empty = (await client.GetFromJsonAsync<LibraryListResponseDto>("/api/library"))!;
        Assert.Empty(empty.Items); Assert.Equal(0, empty.Counts.Total);
        using var scope = context.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.LibraryEvents.CountAsync()); // added, two actual changes, removed
        Assert.Empty(await db.LibraryEntries.ToListAsync());
    }

    [Fact]
    public async Task Library_IsPrivateEvenWhenOtherUserIsAdmin_AndIgnoresSuppliedUserId()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var other = await context.AuthenticatedClientAsync("Admin");
        var id = await OpenGame(owner);
        await Add(owner, id, "playing");
        var list = (await other.GetFromJsonAsync<LibraryListResponseDto>("/api/library?userId=1"))!;
        Assert.Empty(list.Items); Assert.Equal(0, list.Counts.Total);
        using var read = await other.GetAsync($"/api/library/games/{id}");
        using var update = await other.PutAsJsonAsync($"/api/library/{id}/status", new { status = "completed", userId = 1 });
        using var remove = await other.DeleteAsync($"/api/library/{id}");
        foreach (var response in new[] { read, update, remove })
            await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "not_in_library");
        using var ownAdd = await other.PostAsJsonAsync("/api/library", new { gameId = id, status = "backlog", userId = 1 });
        Assert.Equal(HttpStatusCode.Created, ownAdd.StatusCode);
        Assert.Equal("playing", (await owner.GetFromJsonAsync<LibraryEntryDto>($"/api/library/games/{id}"))!.Status);
        Assert.Equal("backlog", (await other.GetFromJsonAsync<LibraryEntryDto>($"/api/library/games/{id}"))!.Status);
    }

    [Theory]
    [InlineData("{}", "invalid_status")]
    [InlineData("{\"status\":\"invalid\"}", "invalid_status")]
    [InlineData("{\"status\":1}", "invalid_status")]
    [InlineData("{\"status\":\"Playing\"}", "invalid_status")]
    [InlineData("{\"status\":\"playing\",\"occurredOn\":\"2026-02-30\"}", "invalid_date")]
    [InlineData("{\"status\":\"playing\",\"occurredOn\":\"2026-1-2\"}", "invalid_date")]
    [InlineData("{\"status\":\"playing\",\"occurredOn\":\"2026-01-02T00:00:00Z\"}", "invalid_date")]
    [InlineData("{\"status\":\"playing\",\"occurredOn\":123}", "invalid_date")]
    [InlineData("{\"status\":\"playing\",\"occurredOn\":\"1969-12-31\"}", "invalid_date")]
    [InlineData("{\"status\":\"playing\",\"occurredOn\":\"2999-12-31\"}", "invalid_date")]
    public async Task InvalidPayloads_UsePublicCodesWithoutMutating(string body, string code)
    {
        using var context = new GamesApiTestContext();
        using var client = await context.AuthenticatedClientAsync();
        var id = await OpenGame(client);
        var payload = JsonNode.Parse(body)!.AsObject();
        payload["gameId"] = id;
        using var post = await client.PostAsync("/api/library", new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        await GamesEndpointsTests.AssertProblemAsync(post, HttpStatusCode.BadRequest, code);
        await Add(client, id, "backlog");
        using var put = await client.PutAsync($"/api/library/{id}/status", new StringContent(body, Encoding.UTF8, "application/json"));
        await GamesEndpointsTests.AssertProblemAsync(put, HttpStatusCode.BadRequest, code);
        Assert.Equal("backlog", (await client.GetFromJsonAsync<LibraryEntryDto>($"/api/library/games/{id}"))!.Status);
    }

    [Fact]
    public async Task MissingGame_Returns404AndDoesNotImportAnExternalId()
    {
        using var context = new GamesApiTestContext(); using var client = await context.AuthenticatedClientAsync();
        using var response = await client.PostAsJsonAsync("/api/library", new { gameId = 19, status = "playing" });
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "game_not_found");
        Assert.Equal(0, context.Provider.DetailCalls);
    }

    [Theory]
    [InlineData("?status=invalid")]
    [InlineData("?sort=invalid")]
    [InlineData("?page=0")]
    [InlineData("?page=abc")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=41")]
    [InlineData("?q=line%0Abreak")]
    public async Task InvalidQuery_Is400InvalidQuery(string query)
    {
        using var context = new GamesApiTestContext(); using var client = await context.AuthenticatedClientAsync();
        using var response = await client.GetAsync("/api/library" + query);
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_query");
    }

    [Fact]
    public async Task FiltersPaginationAndAllSorts_PreserveGlobalCountsAndStableOrder()
    {
        using var context = new GamesApiTestContext(); using var client = await context.AuthenticatedClientAsync();
        var added = new List<LibraryEntryDto>();
        for (var index = 0; index < 6; index++) added.Add(await Add(client, await OpenGame(client, index + 1), States[index % States.Length], $"2026-01-{index + 1:D2}"));
        foreach (var sort in new[] { "recent", "added", "name", "release" })
        {
            var actual = new List<LibraryEntryDto>();
            for (var page = 1; page <= 3; page++)
            {
                var response = (await client.GetFromJsonAsync<LibraryListResponseDto>($"/api/library?sort={sort}&page={page}&pageSize=2"))!;
                Assert.Equal(new LibraryCountsDto(2, 1, 1, 1, 1), response.Counts);
                Assert.Equal(page < 3, response.HasMore); actual.AddRange(response.Items);
            }
            var expected = sort switch
            {
                "name" => added.OrderBy(entry => entry.Name, StringComparer.Ordinal).Select(entry => entry.GameId),
                "release" => added.OrderByDescending(entry => entry.ReleaseYear).Select(entry => entry.GameId),
                "added" => added.OrderByDescending(entry => entry.AddedAt).ThenByDescending(entry => entry.GameId).Select(entry => entry.GameId),
                _ => added.OrderByDescending(entry => entry.StatusChangedOn).Select(entry => entry.GameId),
            };
            Assert.Equal(expected, actual.Select(entry => entry.GameId));
        }
        var filter = (await client.GetFromJsonAsync<LibraryListResponseDto>("/api/library?status=abandoned&q=%20pOrTaL%20"))!;
        Assert.Single(filter.Items); Assert.Equal(6, filter.Counts.Total);
        foreach (var query in new[] { "?q=%25", "?q=_%27%20OR%201%3D1", "?page=2147483647&pageSize=40" })
        {
            var empty = (await client.GetFromJsonAsync<LibraryListResponseDto>("/api/library" + query))!;
            Assert.Empty(empty.Items); Assert.False(empty.HasMore); Assert.Equal(6, empty.Counts.Total);
        }
    }

    [Fact]
    public async Task ConcurrentModification_Is409ProblemDetails()
    {
        using var context = new GamesApiTestContext(); using var authenticated = await context.AuthenticatedClientAsync();
        var service = new Mock<ILibraryService>();
        service.Setup(s => s.ChangeStatusAsync(It.IsAny<int>(), 17, LibraryStatus.Completed, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LibraryException("concurrent_modification"));
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<ILibraryService>(); services.AddSingleton(service.Object); }));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        using var response = await client.PutAsJsonAsync("/api/library/17/status", new { status = "completed" });
        await GamesEndpointsTests.AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrent_modification");
    }

    [Fact]
    public async Task LibraryUsesExistingGlobalRateLimit_WithRetryAfterAndIndependentUsers()
    {
        using var context = new GamesApiTestContext(enableLimits: true, catalogLimit: 100);
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(settings => settings.Global = new(2, TimeSpan.FromMinutes(1)))));
        using var original = await context.AuthenticatedClientAsync();
        using var secondOriginal = await context.AuthenticatedClientAsync();
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = original.DefaultRequestHeaders.Authorization;
        using var second = factory.CreateClient(); second.DefaultRequestHeaders.Authorization = secondOriginal.DefaultRequestHeaders.Authorization;
        using var first = await client.GetAsync("/api/library"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var lookup = await client.GetAsync("/api/library/games/17"); Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/api/library", new { gameId = 17, status = "playing" });
        await GamesEndpointsTests.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        using var independent = await second.GetAsync("/api/library"); Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
    }
}
