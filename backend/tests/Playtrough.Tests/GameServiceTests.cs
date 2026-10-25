using Microsoft.Extensions.DependencyInjection;
using Moq;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;
using Xunit;

namespace Playtrough.Tests;

public class GameServiceTests
{
    [Fact]
    public async Task FirstUse_PersistsAllDetails_ThenReadsFreshLocalCopyWithoutProvider()
    {
        using var context = new GamePersistenceTestContext();
        var created = await context.GetAsync();
        var again = await context.GetAsync(provider: " RAWG ");
        var stored = Assert.Single(await context.ReadAsync());
        var details = GamePersistenceTestContext.Details();
        Assert.True(created.Id > 0);
        Assert.Equal(created.Id, again.Id);
        Assert.Equal("rawg", stored.Provider);
        Assert.Equal(42, stored.ExternalId);
        Assert.Equal(details.Name, stored.Name);
        Assert.Equal(details.Slug, stored.Slug);
        Assert.Equal(details.Summary, stored.Summary);
        Assert.Equal(details.CoverUrlLarge, stored.CoverImageUrl);
        Assert.Equal(details.ReleaseDate, stored.FirstReleaseDate);
        Assert.Equal(details.Platforms, stored.Platforms);
        Assert.Equal(details.Genres, stored.Genres);
        Assert.Equal(details.Developers, stored.Developers);
        Assert.Equal(context.Clock.GetUtcNow().UtcDateTime, stored.CreatedAt);
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, stored.UpdatedAt.Kind);
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SimultaneousRequests_UseIndependentConnections_AndRecoverActualUniqueConflicts()
    {
        const int count = 8;
        using var context = new GamePersistenceTestContext(concurrent: true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = 0;
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref waiting) == count) entered.SetResult();
                await release.Task.WaitAsync(token);
                return GamePersistenceTestContext.Details(id);
            });
        var requests = Enumerable.Range(0, count).Select(_ => Task.Run(() => context.GetAsync())).ToArray();
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { release.TrySetResult(); }
        var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
        var stored = Assert.Single(await context.ReadAsync());
        Assert.All(results, game => Assert.Equal(stored.Id, game.Id));
        Assert.Equal(count - 1, context.InsertFailures.UniqueConflicts);
    }

    [Fact]
    public async Task StaleLocalGame_RefreshesFieldsAndTimestamp_AndPreservesLocalIdentityAndCreation()
    {
        using var context = new GamePersistenceTestContext();
        var old = await context.SeedAsync(TimeSpan.FromDays(31));
        var refreshed = await context.GetAsync();
        var stored = Assert.Single(await context.ReadAsync());
        Assert.Equal(old.Id, refreshed.Id);
        Assert.Equal(old.CreatedAt, stored.CreatedAt);
        Assert.Equal(context.Clock.GetUtcNow().UtcDateTime, stored.UpdatedAt);
        Assert.Equal(GamePersistenceTestContext.Details().Name, stored.Name);
        Assert.Equal(GamePersistenceTestContext.Details().Genres, stored.Genres);
        Assert.Equal(GamePersistenceTestContext.Details().Developers, stored.Developers);
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StaleCopy_WhenProviderUnavailable_ReturnsUnchangedCopy_AndTriesAgainNextTime()
    {
        using var context = new GamePersistenceTestContext();
        var old = await context.SeedAsync(TimeSpan.FromDays(40));
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GameCatalogUnavailableException());
        var fallback = await context.GetAsync();
        await context.GetAsync();
        var stored = Assert.Single(await context.ReadAsync());
        Assert.Equal(old.Id, fallback.Id);
        Assert.Equal(old.Name, fallback.Name);
        Assert.Equal(old.UpdatedAt, fallback.UpdatedAt);
        Assert.Equal(old.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(old.Summary, stored.Summary);
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task FreshCopy_DoesNotContactUnavailableProvider()
    {
        using var context = new GamePersistenceTestContext();
        var local = await context.SeedAsync(TimeSpan.FromDays(1));
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GameCatalogUnavailableException());
        Assert.Equal(local.Id, (await context.GetAsync()).Id);
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoLocalCopy_WhenProviderUnavailable_PropagatesAndDoesNotPersistAnything()
    {
        using var context = new GamePersistenceTestContext();
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GameCatalogUnavailableException());
        await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.GetAsync());
        Assert.Empty(await context.ReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderReportsMissingGame_ThrowsNotFound_EvenWithStaleCopy(bool hasCopy)
    {
        using var context = new GamePersistenceTestContext();
        if (hasCopy) await context.SeedAsync(TimeSpan.FromDays(31));
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDetails?)null);
        await Assert.ThrowsAsync<GameNotFoundException>(() => context.GetAsync());
        Assert.Equal(hasCopy ? 1 : 0, (await context.ReadAsync()).Count);
    }

    [Fact]
    public async Task RefreshAge_IsConfigurable_AndExactThresholdIsStillFresh()
    {
        using var context = new GamePersistenceTestContext(refreshAfterDays: 2);
        var old = await context.SeedAsync(TimeSpan.FromDays(2));
        Assert.Equal(old.Name, (await context.GetAsync()).Name);
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Clock.UtcNow += TimeSpan.FromTicks(1);
        Assert.Equal(GamePersistenceTestContext.Details().Name, (await context.GetAsync()).Name);
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshWithinOneScope_CanFollowAnEarlierInsert()
    {
        using var context = new GamePersistenceTestContext();
        using var scope = context.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IGameService>();
        var first = await service.GetOrCreateAsync("rawg", 42);
        context.Clock.UtcNow += TimeSpan.FromDays(31);
        var second = await service.GetOrCreateAsync("rawg", 42);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await context.ReadAsync());
        context.Provider.Verify(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProviderCancellation_PropagatesInsteadOfReturningStaleCopy()
    {
        using var context = new GamePersistenceTestContext();
        var old = await context.SeedAsync(TimeSpan.FromDays(31));
        using var cancellation = new CancellationTokenSource();
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<GameDetails?>(token);
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.GetAsync(cancellationToken: cancellation.Token));
        Assert.Equal(old.UpdatedAt, Assert.Single(await context.ReadAsync()).UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchedProviderId_IsUnavailable_OrFallsBackToExistingCopy(bool hasCopy)
    {
        using var context = new GamePersistenceTestContext();
        if (hasCopy) await context.SeedAsync(TimeSpan.FromDays(31));
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GamePersistenceTestContext.Details(999));
        if (hasCopy) Assert.Equal(42, (await context.GetAsync()).ExternalId);
        else await Assert.ThrowsAsync<GameCatalogUnavailableException>(() => context.GetAsync());
        Assert.DoesNotContain(await context.ReadAsync(), game => game.ExternalId == 999);
    }

    [Theory]
    [InlineData("rawg", 0)]
    [InlineData("rawg", -1)]
    [InlineData("unknown", 42)]
    public async Task InvalidIdentity_IsRejectedWithoutProviderOrPersistence(string provider, int id)
    {
        using var context = new GamePersistenceTestContext();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => context.GetAsync(id, provider));
        context.Provider.VerifyNoOtherCalls();
        Assert.Empty(await context.ReadAsync());
    }

    [Fact]
    public async Task NullOptionalDetails_AndEmptyCollections_ArePersisted()
    {
        using var context = new GamePersistenceTestContext();
        context.Provider.Setup(provider => provider.GetByExternalIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDetails(42, "Minimal", "minimal", null, null, [], "", [], [], null));
        await context.GetAsync();
        var stored = Assert.Single(await context.ReadAsync());
        Assert.Null(stored.CoverImageUrl);
        Assert.Null(stored.FirstReleaseDate);
        Assert.Empty(stored.Summary);
        Assert.Empty(stored.Platforms);
        Assert.Empty(stored.Genres);
        Assert.Empty(stored.Developers);
    }
}
