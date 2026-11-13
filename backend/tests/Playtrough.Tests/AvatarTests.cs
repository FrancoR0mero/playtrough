using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Controllers;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;
using SkiaSharp;
using Xunit;

namespace Playtrough.Tests;

public class AvatarTests
{
    private const string Endpoint = "/api/users/me/avatar";

    [Theory]
    [InlineData("image/png", "photo.png", SKEncodedImageFormat.Png)]
    [InlineData("image/jpeg", "photo.JPG", SKEncodedImageFormat.Jpeg)]
    [InlineData("image/webp", "photo.webp", SKEncodedImageFormat.Webp)]
    public async Task RealUploads_NormalizeCropAndExposeAvatars_ReplacementAndDeletionCleanFiles(string mime, string name, SKEncodedImageFormat format)
    {
        using var f = new AvatarContext();
        using var owner = await f.Api.AuthenticatedClientAsync();
        using var viewer = await f.Api.AuthenticatedClientAsync();
        var original = Picture(format);
        var first = await Upload(owner, original, name, mime);
        Assert.NotNull(first.AvatarUrl);
        using var anonymous = f.Api.Factory.CreateClient();
        using var served = await anonymous.GetAsync(first.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/webp", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        using (var image = SKBitmap.Decode(await served.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal(256, image.Width);
            Assert.Equal(256, image.Height);
            Assert.True(image.GetPixel(128, 128).Green > 180);
        }
        Assert.Single(Directory.GetFiles(f.Root));
        Assert.Equal(first.AvatarUrl, (await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.AvatarUrl);
        await PrivacyEndpointsTests.Set(owner, "private", "private");
        Assert.Equal(first.AvatarUrl, (await viewer.GetFromJsonAsync<ProfileDto>("/api/users/games_user_1"))!.AvatarUrl);
        Assert.Equal(first.AvatarUrl, Assert.Single((await viewer.GetFromJsonAsync<UserSearchResponse>("/api/users/search?q=games_user_1"))!.Items).AvatarUrl);
        using var sent = await owner.PostAsJsonAsync("/api/friends/requests", new { username = "games_user_2" });
        var request = (await sent.Content.ReadFromJsonAsync<SentFriendRequestDto>())!;
        var received = (await viewer.GetFromJsonAsync<FriendsPage<FriendRequestDto>>("/api/friends/requests/received"))!;
        Assert.Equal(first.AvatarUrl, Assert.Single(received.Items).User.AvatarUrl);
        (await viewer.PostAsync($"/api/friends/requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        var friends = (await viewer.GetFromJsonAsync<FriendsPage<FriendDto>>("/api/friends"))!;
        Assert.Equal(first.AvatarUrl, Assert.Single(friends.Items).User.AvatarUrl);
        using var login = await f.Api.LoginClientAsync();
        var second = await Upload(owner, original, name, mime);
        Assert.NotEqual(first.AvatarUrl, second.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.AvatarUrl)).StatusCode);
        Assert.Single(Directory.GetFiles(f.Root));
        Assert.Null((await viewer.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.AvatarUrl);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(second.AvatarUrl)).StatusCode);
        Assert.Null((await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.AvatarUrl);
        Assert.Empty(Directory.GetFiles(f.Root));
    }

    [Theory]
    [InlineData("image/png", "photo.png", "<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("image/png", "photo.png", "not an image")]
    [InlineData("image/png", "photo.png", "")]
    public async Task InvalidContent_IsRejectedWithoutChangingExistingAvatar(string mime, string name, string body)
    {
        using var f = new AvatarContext();
        using var owner = await f.Api.AuthenticatedClientAsync();
        var previous = await Upload(owner, Picture(), "photo.png", "image/png");
        using var content = Multipart(Encoding.UTF8.GetBytes(body), name, mime);
        await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, content), 400, "invalid_avatar");
        Assert.Equal(previous.AvatarUrl, (await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.AvatarUrl);
        Assert.Single(Directory.GetFiles(f.Root));
    }

    [Theory]
    [InlineData(32, 100)]
    [InlineData(4200, 100)]
    [InlineData(4096, 4096)]
    public async Task Dimensions_AreCheckedBeforeDecoding(int width, int height)
    {
        using var f = new AvatarContext();
        using var owner = await f.Api.AuthenticatedClientAsync();
        using var content = Multipart(Picture(width: width, height: height), "photo.png", "image/png");
        await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, content), 400, "invalid_avatar_dimensions");
        Assert.False(Directory.Exists(f.Root));
    }

    [Fact]
    public async Task MimeExtensionMissingFileAndOversize_AreRejected()
    {
        using var f = new AvatarContext();
        using var owner = await f.Api.AuthenticatedClientAsync();
        foreach (var (name, mime) in new[] { ("photo.jpg", "image/jpeg"), ("photo.svg", "image/png"), ("photo.png", "text/plain") })
        {
            using var content = Multipart(Picture(), name, mime);
            await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, content), 400, "invalid_avatar");
        }
        using (var empty = new MultipartFormDataContent())
            await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, empty), 400, "invalid_avatar");
        using (var large = Multipart(new byte[AvatarSettings.MaxFileBytes + 1], "large.png", "image/png"))
            await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, large), 413, "avatar_too_large");
        using (var huge = Multipart(new byte[AvatarSettings.MaxRequestBytes + 1], "large.png", "image/png"))
            await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, huge), 413, "payload_too_large");
        using (var duplicate = Multipart(Picture(), "photo.png", "image/png"))
        {
            duplicate.Add(new ByteArrayContent(Picture()), "file", "second.png");
            await ProfileEndpointsTests.Error(await owner.PostAsync(Endpoint, duplicate), 400, "invalid_avatar");
        }
        Assert.False(Directory.Exists(f.Root));
    }

    [Fact]
    public async Task AuthenticationAndServingPaths_AreProtected()
    {
        using var f = new AvatarContext();
        using var anonymous = f.Api.Factory.CreateClient();
        using var upload = Multipart(Picture(), "photo.png", "image/png");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(Endpoint, upload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/avatars/appsettings.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/avatars/invalid.webp")).StatusCode);
        Assert.Null(f.Api.Factory.Services.GetRequiredService<IAvatarStorage>().OpenRead("../appsettings.json"));
    }

    [Fact]
    public async Task OrientationIsApplied_AndExifIsNotRetained()
    {
        using var bitmap = new SKBitmap(128, 256);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 128, 128, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        var bytes = jpeg.ToArray();
        // Minimal EXIF APP1: little-endian TIFF with Orientation=6 (90 degrees clockwise).
        byte[] exif = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0,
            18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var oriented = bytes[..2].Concat(exif).Concat(bytes[2..]).ToArray();
        using var stream = new MemoryStream(oriented);
        var file = new FormFile(stream, 0, oriented.Length, "file", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
        var processor = new AvatarImageProcessor();
        var output = await processor.ProcessAsync(file, default);
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(output));
        using var result = SKBitmap.Decode(output);
        Assert.True(result.GetPixel(32, 128).Blue > 180);
        Assert.True(result.GetPixel(224, 128).Red > 180);
    }

    [Fact]
    public async Task ConcurrentProfileWrite_RejectsAvatarAndCleansNewFile()
    {
        using var f = new AvatarContext();
        using var client = await f.Api.AuthenticatedClientAsync();
        using var scope = f.Api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.UserName == "games_user_1");
        var real = f.Api.Factory.Services.GetRequiredService<IAvatarStorage>();
        var storage = new Mock<IAvatarStorage>();
        storage.Setup(s => s.SaveAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).Returns(async (byte[] image, CancellationToken ct) =>
        {
            var name = await real.SaveAsync(image, ct);
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(update => update.SetProperty(u => u.ConcurrencyStamp, "new-stamp"));
            return name;
        });
        storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string name, CancellationToken ct) => real.DeleteAsync(name, ct));
        var service = new AvatarService(db, new AvatarImageProcessor(), storage.Object,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AvatarService>>());
        var bytes = Picture();
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "photo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var error = await Assert.ThrowsAsync<ProfileException>(() => service.UploadAsync(user.Id, file, default));
        Assert.Equal("concurrent_modification", error.Code);
        Assert.Empty(Directory.GetFiles(f.Root));
        Assert.Null(await db.Users.Where(u => u.Id == user.Id).Select(u => u.AvatarFileName).SingleAsync());
    }

    [Fact]
    public async Task StaleIdentityWrites_CannotRestoreADeletedAvatar()
    {
        using var f = new AvatarContext();
        using var owner = await f.Api.AuthenticatedClientAsync();
        await Upload(owner, Picture(), "photo.png", "image/png");
        using var scope = f.Api.Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stale = (await manager.FindByNameAsync("games_user_1"))!;
        (await owner.DeleteAsync(Endpoint)).EnsureSuccessStatusCode();
        stale.Bio = "stale edit";
        var result = await manager.UpdateAsync(stale);
        Assert.Contains(result.Errors, error => error.Code == "ConcurrencyFailure");
        Assert.Null((await owner.GetFromJsonAsync<MyProfileDto>("/api/users/me"))!.AvatarUrl);
    }

    [Fact]
    public async Task AvatarWrites_ShareProfileBudget_AndReadRemainsAvailable()
    {
        using var f = new AvatarContext(enableLimits: true);
        using var owner = await f.Api.AuthenticatedClientAsync();
        var avatar = await Upload(owner, Picture(), "photo.png", "image/png");
        for (var i = 0; i < 9; i++)
            (await owner.PutAsJsonAsync("/api/users/me", new { bio = i.ToString() })).EnsureSuccessStatusCode();
        using var content = Multipart(Picture(), "photo.png", "image/png");
        using var response = await owner.PostAsync(Endpoint, content);
        await ProfileEndpointsTests.Error(response, 429, "rate_limited");
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        await ProfileEndpointsTests.Error(await owner.DeleteAsync(Endpoint), 429, "rate_limited");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(avatar.AvatarUrl)).StatusCode);
    }

    [Fact]
    public async Task Swagger_DescribesMultipartUploadAndPublicWebp()
    {
        using var c = new GamesApiTestContext(environment: "Development");
        using var client = c.Factory.CreateClient();
        using var doc = (await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json"))!;
        var paths = doc.RootElement.GetProperty("paths");
        var upload = paths.GetProperty(Endpoint).GetProperty("post");
        Assert.True(upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        Assert.True(upload.GetProperty("responses").TryGetProperty("413", out _));
        var download = paths.GetProperty("/api/avatars/{fileName}").GetProperty("get");
        Assert.True(download.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("image/webp", out _));
    }

    [Fact]
    public void Migration_IsNullable_AndSnapshotMatches()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var sql = db.GetService<IMigrator>().GenerateScript("20261108005013_AddUserPrivacy", "AddUserAvatar");
        Assert.Contains("ADD `AvatarFileName` varchar(37) NULL", sql);
        Assert.DoesNotContain("UPDATE `Users`", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    internal static byte[] Picture(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 320, int height = 160)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Lime);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static MultipartFormDataContent Multipart(byte[] bytes, string name, string mime)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        content.Add(file, "file", name);
        return content;
    }

    private static async Task<AvatarDto> Upload(HttpClient client, byte[] bytes, string name, string mime)
    {
        using var content = Multipart(bytes, name, mime);
        using var response = await client.PostAsync(Endpoint, content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AvatarDto>())!;
    }

    private sealed class AvatarContext : IDisposable
    {
        public GamesApiTestContext Api { get; }
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"playtrough-avatar-tests-{Guid.NewGuid():N}");
        public AvatarContext(bool enableLimits = false)
        {
            Api = new GamesApiTestContext(enableLimits: enableLimits);
            Api.Factory.Services.GetRequiredService<IOptions<AvatarSettings>>().Value.StoragePath = Root;
        }
        public void Dispose()
        {
            Api.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
