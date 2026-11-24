using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class PrivacyServiceTests
{
    [Theory]
    [InlineData(Visibility.Public, Visibility.Public)]
    [InlineData(Visibility.Public, Visibility.Friends)]
    [InlineData(Visibility.Public, Visibility.Private)]
    [InlineData(Visibility.Friends, Visibility.Public)]
    [InlineData(Visibility.Private, Visibility.Public)]
    [InlineData(Visibility.Friends, Visibility.Private)]
    public async Task LegacySettings_UseOneConservativePolicy_AndSaveTogether(Visibility profile, Visibility history)
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb();
        await db.Users.Where(u => u.Id == 1).ExecuteUpdateAsync(update => update
            .SetProperty(u => u.ProfileVisibility, profile).SetProperty(u => u.HistoryVisibility, history));
        db.Friendships.Add(new Friendship { UserLowId = 1, UserHighId = 2, CreatedAt = c.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        var service = new PrivacyService(db);
        var isPublic = profile == Visibility.Public && history == Visibility.Public;
        Assert.Equal(new PrivacyDto(isPublic ? "public" : "private"), await service.GetAsync(1));
        foreach (var scope in Enum.GetValues<PrivacyScope>())
        {
            Assert.True(await service.CanViewAsync(1, 1, scope));
            Assert.True(await service.CanViewAsync(2, 1, scope));
            Assert.Equal(isPublic, await service.CanViewAsync(3, 1, scope));
        }
        await service.UpdateAsync(1, Request("private"));
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 1);
        Assert.Equal(Visibility.Private, row.ProfileVisibility);
        Assert.Equal(Visibility.Private, row.HistoryVisibility);
    }

    public static IEnumerable<object[]> Matrix =>
        from scope in Enum.GetValues<PrivacyScope>()
        from visibility in Enum.GetValues<Visibility>()
        from viewer in new[] { 1, 2, 3 }
        select new object[] { scope, visibility, viewer,
            viewer == 1 || visibility == Visibility.Public || visibility != Visibility.Public && viewer == 2 };

    [Theory, MemberData(nameof(Matrix))]
    public async Task CompleteMatrix_OwnerFriendAndStranger_ForBothScopes(PrivacyScope scope, Visibility visibility, int viewer, bool expected)
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb();
        await db.Users.Where(u => u.Id == 1).ExecuteUpdateAsync(update => update
            .SetProperty(u => u.ProfileVisibility, visibility)
            .SetProperty(u => u.HistoryVisibility, visibility));
        db.Friendships.Add(new Friendship { UserLowId = 1, UserHighId = 2, CreatedAt = c.Clock.UtcNow.UtcDateTime });
        await db.SaveChangesAsync();
        c.Observer.Commands.Clear();
        Assert.Equal(expected, await new PrivacyService(db).CanViewAsync(viewer, 1, scope));
        var sql = Assert.Single(c.Observer.Commands);
        Assert.Equal(viewer == 1 ? 0 : 1, sql.Split("FROM \"Friendships\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task SameServiceInstance_DoesNotCacheTrackedVisibilityOrFriendship()
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb();
        var service = new PrivacyService(db);
        await db.Users.SingleAsync(u => u.Id == 1); // Deliberately track a stale user.
        await service.UpdateAsync(1, Request("private"));
        Assert.False(await service.CanViewAsync(2, 1, PrivacyScope.Profile));
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.AcceptAsync(2, request.Id));
        Assert.True(await service.CanViewAsync(2, 1, PrivacyScope.Profile));
        Assert.True(await service.CanViewAsync(2, 1, PrivacyScope.History));
        await c.Run(s => s.RemoveFriendAsync(2, FriendTestContext.Name(1)));
        Assert.False(await service.CanViewAsync(2, 1, PrivacyScope.Profile));
        Assert.False(await service.CanViewAsync(2, 1, PrivacyScope.History));
        await using (var otherDb = c.CreateDb())
            await new PrivacyService(otherDb).UpdateAsync(1, Request("public"));
        Assert.True(await service.CanViewAsync(2, 1, PrivacyScope.Profile));
        Assert.True(await service.CanViewAsync(2, 1, PrivacyScope.History));
        Assert.False(await service.CanViewAsync(0, 1, PrivacyScope.Profile));
    }

    [Fact]
    public async Task UpdatingPrivacy_IsAtomicAndDoesNotOverwriteOtherUserFields()
    {
        using var c = new FriendTestContext();
        await using var db = c.CreateDb();
        await db.Users.Where(u => u.Id == 1).ExecuteUpdateAsync(update => update.SetProperty(u => u.Bio, "kept"));
        var service = new PrivacyService(db);
        var before = await service.GetAsync(1);
        var invalid = await Assert.ThrowsAsync<ProfileException>(() => service.UpdateAsync(1, Request("invalid")));
        Assert.Equal("invalid_visibility", invalid.Code);
        Assert.Equal(before, await service.GetAsync(1));
        Assert.Equal(new PrivacyDto("private"), await service.UpdateAsync(1, Request("private")));
        Assert.Equal("kept", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == 1)).Bio);
        Assert.Equal(new PrivacyDto("public"), await service.GetAsync(2));
        Assert.Equal("user_not_found", (await Assert.ThrowsAsync<ProfileException>(() => service.GetAsync(999))).Code);
    }

    internal static UpdatePrivacyRequest Request(string visibility) => new()
    {
        Visibility = JsonSerializer.SerializeToElement(visibility),
    };
}
