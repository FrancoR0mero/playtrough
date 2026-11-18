using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Xunit;

namespace Playtrough.Tests;

public sealed class TimelineCommentEndpointsTests
{
    [Fact]
    public async Task CommentsAndDetailUseTheAuthenticatedViewer()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        using var author = await context.AuthenticatedClientAsync();
        using var stranger = await context.AuthenticatedClientAsync();
        var entryId = await CreateEntry(owner);
        var entry = $"/api/timeline/entries/{entryId}";
        var collection = entry + "/comments";
        using var added = await author.PostAsJsonAsync(collection, new { text = " Hola 🎮 ", authorId = 1 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var created = (await added.Content.ReadFromJsonAsync<CommentCreatedDto>())!;
        Assert.Equal("games_user_2", created.Comment.Author.Username);
        Assert.Equal("Hola 🎮", created.Comment.Text);
        Assert.Equal(1, created.CommentCount);

        using var reaction = await author.PutAsJsonAsync(entry + "/reaction", new { value = "like" });
        reaction.EnsureSuccessStatusCode();
        var detail = (await author.GetFromJsonAsync<FeedEntryDto>(entry))!;
        Assert.Equal(entryId, detail.Id);
        Assert.Equal("games_user_1", detail.Author.Username);
        Assert.Equal((1, 0, 1, "like"), (detail.LikeCount, detail.DislikeCount, detail.CommentCount, detail.MyReaction));
        var ownerDetail = (await owner.GetFromJsonAsync<FeedEntryDto>(entry))!;
        Assert.Null(ownerDetail.MyReaction);
        var page = (await owner.GetFromJsonAsync<CommentPageDto>(collection))!;
        var listed = Assert.Single(page.Items);
        Assert.False(listed.IsMine);
        Assert.True(listed.CanDelete);

        var item = $"/api/timeline/comments/{listed.Id}";
        using var wrongEditor = await owner.PutAsJsonAsync(item, new { text = "editado" });
        await ProfileEndpointsTests.Error(wrongEditor, 404, "comment_not_found");
        using var edited = await author.PutAsJsonAsync(item, new { text = "editado" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.NotNull((await edited.Content.ReadFromJsonAsync<CommentDto>())!.EditedAt);
        using var wrongDeleter = await stranger.DeleteAsync(item);
        await ProfileEndpointsTests.Error(wrongDeleter, 404, "comment_not_found");
        using var deleted = await owner.DeleteAsync(item);
        deleted.EnsureSuccessStatusCode();
        Assert.Equal(0, (await deleted.Content.ReadFromJsonAsync<CommentCountDto>())!.CommentCount);
        Assert.Empty((await author.GetFromJsonAsync<CommentPageDto>(collection))!.Items);
    }

    [Fact]
    public async Task InvalidBodiesPaginationAndMissingEntriesReturnStableCodes()
    {
        using var context = new GamesApiTestContext();
        using var owner = await context.AuthenticatedClientAsync();
        var entryId = await CreateEntry(owner);
        var collection = $"/api/timeline/entries/{entryId}/comments";
        foreach (var json in new[] { "", "{", "{}", """{"text":null}""", """{"text":1}""", """{"text":" "}""" })
        {
            using var invalid = await owner.PostAsync(collection, new StringContent(json, Encoding.UTF8, "application/json"));
            await ProfileEndpointsTests.Error(invalid, 400, "invalid_comment");
        }

        using var badCursor = await owner.GetAsync(collection + "?cursor=");
        await ProfileEndpointsTests.Error(badCursor, 400, "invalid_cursor");
        using var badLimit = await owner.GetAsync(collection + "?limit=51");
        await ProfileEndpointsTests.Error(badLimit, 400, "invalid_filter");
        using var added = await owner.PostAsJsonAsync(collection, new { text = "mismo texto" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var duplicate = await owner.PostAsJsonAsync(collection, new { text = " mismo texto " });
        await ProfileEndpointsTests.Error(duplicate, 409, "duplicate_comment");
        using var missing = await owner.GetAsync("/api/timeline/entries/9223372036854775807");
        await ProfileEndpointsTests.Error(missing, 404, "entry_not_found");
    }

    [Fact]
    public async Task AllRoutesRequireAuthenticationAndWritesShareTheUserBudget()
    {
        using var context = new GamesApiTestContext(enableLimits: true);
        using var first = await context.AuthenticatedClientAsync();
        using var second = await context.AuthenticatedClientAsync();
        var entryId = await CreateEntry(first);
        using var factory = context.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<RateLimitSettings>(options => options.CommentsWrite = new(2, TimeSpan.FromMinutes(1)))));
        using var actor = factory.CreateClient();
        using var another = factory.CreateClient();
        using var guest = factory.CreateClient();
        actor.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
        another.DefaultRequestHeaders.Authorization = second.DefaultRequestHeaders.Authorization;
        var entry = $"/api/timeline/entries/{entryId}";
        var collection = entry + "/comments";
        using var guestDetail = await guest.GetAsync(entry);
        using var guestList = await guest.GetAsync(collection);
        using var guestAdd = await guest.PostAsJsonAsync(collection, new { text = "Texto" });
        Assert.All(new[] { guestDetail, guestList, guestAdd }, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));

        using var added = await actor.PostAsJsonAsync(collection, new { text = "Primero" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var comment = (await added.Content.ReadFromJsonAsync<CommentCreatedDto>())!.Comment;
        var item = $"/api/timeline/comments/{comment.Id}";
        using var guestEdit = await guest.PutAsJsonAsync(item, new { text = "Texto" });
        using var guestDelete = await guest.DeleteAsync(item);
        Assert.All(new[] { guestEdit, guestDelete }, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));

        using var edited = await actor.PutAsJsonAsync(item, new { text = "Editado" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var limited = await actor.DeleteAsync(item);
        await ProfileEndpointsTests.Error(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var independent = await another.PostAsJsonAsync(collection, new { text = "Segundo" });
        Assert.Equal(HttpStatusCode.Created, independent.StatusCode);
        Assert.Equal(2, (await actor.GetFromJsonAsync<CommentPageDto>(collection))!.CommentCount);
    }

    [Fact]
    public async Task SwaggerDocumentsCommentOperationsAndEntryPrivacy()
    {
        using var context = new GamesApiTestContext(environment: "Development");
        using var client = context.Factory.CreateClient();
        using var document = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty("/api/timeline/entries/{id}/comments");
        var item = paths.GetProperty("/api/timeline/comments/{id}");
        foreach (var operation in new[] { collection.GetProperty("get"), collection.GetProperty("post"),
                     item.GetProperty("put"), item.GetProperty("delete") })
        {
            Assert.Equal("Comments", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
            Assert.True(operation.GetProperty("responses").TryGetProperty("404", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("429", out _));
        }
        var detail = paths.GetProperty("/api/timeline/entries/{id}").GetProperty("get").GetProperty("responses");
        Assert.Contains("entry_not_found", detail.GetProperty("404").GetProperty("description").GetString());
        Assert.False(detail.TryGetProperty("403", out _));
    }

    private static async Task<long> CreateEntry(HttpClient owner)
    {
        var game = await ReviewEndpointsTests.OpenGameAsync(owner);
        using var added = await owner.PostAsJsonAsync("/api/library", new { gameId = game.Id, status = "playing" });
        added.EnsureSuccessStatusCode();
        return Assert.Single((await owner.GetFromJsonAsync<TimelinePageDto>("/api/timeline/me"))!.Items).Id;
    }
}
