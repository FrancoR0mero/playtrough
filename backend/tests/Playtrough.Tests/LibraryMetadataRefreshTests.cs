using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Playtrough.Api.Catalog;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Library;
using Playtrough.Api.Models;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public sealed class LibraryMetadataRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_RefreshesExpiredOrAlreadyCleanedMetadata_AndReusesFreshCopy(bool cleaned)
    {
        using var context = new LibraryTestContext();
        await context.AddAsync(LibraryStatus.Completed);
        await using var db = context.CreateDb();
        await SetMetadataAsync(context, db, cleaned ? null : context.Clock.UtcNow.UtcDateTime);
        var provider = Provider();
        var service = Service(context, db, provider);

        var response = await service.ListAsync(context.UserId, new());
        var item = Assert.Single(response.Items);
        Assert.Equal("Example Game", item.Name);
        Assert.Equal("https://images.example/original.jpg", item.CoverUrl);
        Assert.Equal(context.GameId, item.GameId);
        Assert.Equal("completed", item.Status);
        Assert.Equal(1, response.Counts.Completed);
        Assert.Single(await context.EventsAsync());
        var stored = await db.Games.AsNoTracking().SingleAsync();
        Assert.Equal(context.Clock.UtcNow.AddHours(24).UtcDateTime, stored.MetadataExpiresAt);
        Assert.Equal(item.Name, stored.Name);

        await service.ListAsync(context.UserId, new());
        provider.Verify(p => p.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("quota")]
    [InlineData("unavailable")]
    [InlineData("authentication")]
    [InlineData("missing")]
    public async Task List_ProviderFailurePreservesLibraryAndStopsOnUnavailableProvider(string failure)
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        await using var db = context.CreateDb();
        await SetMetadataAsync(context, db, context.Clock.UtcNow.AddDays(-1).UtcDateTime);
        var second = GamePersistenceTestContext.LocalGame(context.Clock.UtcNow.UtcDateTime, 43);
        second.Provider = "gamebrain";
        second.GameBrainId = 43;
        db.Games.Add(second);
        await db.SaveChangesAsync();
        await new LibraryService(new LibraryRepository(db, context.Clock), context.Clock,
            Mock.Of<IGameService>()).AddAsync(context.UserId, second.Id, LibraryStatus.Playing);
        var provider = Provider();
        provider.Setup(p => p.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure switch
            {
                "quota" => (Exception)new GameBrainQuotaException(),
                "unavailable" => new GameCatalogUnavailableException(),
                "authentication" => new GameBrainAuthenticationException(),
                _ => new GameNotFoundException(),
            });

        var result = await Service(context, db, provider).ListAsync(context.UserId, new());

        Assert.Equal(2, result.Counts.Total);
        Assert.Equal(2, result.Items.Count);
        var expired = Assert.Single(result.Items, item => item.GameId == context.GameId);
        Assert.Equal("Juego #42", expired.Name);
        Assert.Null(expired.CoverUrl);
        Assert.Equal("backlog", expired.Status);
        Assert.Equal(2, (await context.EventsAsync()).Count);
        provider.Verify(p => p.GetByExternalIdAsync(43, It.IsAny<CancellationToken>()),
            failure == "missing" ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_SkipsFreshMetadataAndGamesWithoutGameBrainIdentity(bool fresh)
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        await using var db = context.CreateDb();
        if (fresh) await SetMetadataAsync(context, db, context.Clock.UtcNow.AddHours(1).UtcDateTime);
        var provider = new Mock<IGameDataProvider>(MockBehavior.Strict);

        var result = await Service(context, db, provider).ListAsync(context.UserId, new());

        Assert.Single(result.Items);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_RefreshesOnlyRequestedPageAndUser_ThenReappliesNameOrdering()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        await using var db = context.CreateDb();
        await SetMetadataAsync(context, db, null);
        var offPage = GamePersistenceTestContext.LocalGame(context.Clock.UtcNow.UtcDateTime, 43);
        offPage.Provider = "gamebrain";
        offPage.GameBrainId = 43;
        offPage.Name = "ZZZ";
        var otherUser = GamePersistenceTestContext.LocalGame(context.Clock.UtcNow.UtcDateTime, 44);
        otherUser.Provider = "gamebrain";
        otherUser.GameBrainId = 44;
        db.Games.AddRange(offPage, otherUser);
        await db.SaveChangesAsync();
        var provider = Provider();
        var service = Service(context, db, provider);
        await service.AddAsync(context.UserId, offPage.Id, LibraryStatus.Playing);
        await service.AddAsync(context.OtherUserId, otherUser.Id, LibraryStatus.Playing);
        // The refreshed name changes which game belongs on the first page.
        provider.Setup(p => p.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GamePersistenceTestContext.Details() with { Name = "ZZZZ" });

        var result = await service.ListAsync(context.UserId, new LibraryListRequest { Sort = "name", PageSize = 1 });

        Assert.Equal(offPage.Id, Assert.Single(result.Items).GameId);
        Assert.True(result.HasMore);
        Assert.Equal(2, result.Counts.Total);
        provider.Verify(p => p.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_DoesNotSwallowRequestCancellation()
    {
        using var context = new LibraryTestContext();
        await context.AddAsync();
        await using var db = context.CreateDb();
        await SetMetadataAsync(context, db, null);
        using var cancellation = new CancellationTokenSource();
        var provider = Provider();
        provider.Setup(p => p.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken ct) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<GameDetails?>(ct);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(context, db, provider).ListAsync(context.UserId, new(), cancellation.Token));
    }

    private static async Task SetMetadataAsync(LibraryTestContext context, AppDbContext db, DateTime? expiry)
    {
        var game = await db.Games.SingleAsync(game => game.Id == context.GameId);
        game.Provider = "gamebrain";
        game.GameBrainId = 42;
        game.MetadataExpiresAt = expiry;
        game.Name = "Juego #42";
        await db.SaveChangesAsync();
    }

    private static Mock<IGameDataProvider> Provider()
    {
        var provider = new Mock<IGameDataProvider>(MockBehavior.Strict);
        provider.Setup(p => p.GetByExternalIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => GamePersistenceTestContext.Details(id));
        return provider;
    }

    private static LibraryService Service(LibraryTestContext context, AppDbContext db, Mock<IGameDataProvider> provider)
        => new(new LibraryRepository(db, context.Clock), context.Clock,
            new GameService(new GameRepository(db), provider.Object, Options.Create(new CatalogSettings()),
                context.Clock, NullLogger<GameService>.Instance));
}
