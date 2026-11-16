using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Users;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class UserSearchTests
{
    [Fact]
    public async Task RankingAndPaging_ExactThenPrefixThenContains_ExcludeOwnerAndUnconfirmed()
    {
        using var c = new FriendTestContext();
        await Rename(c, 1, "foo_owner");
        await Rename(c, 2, "zfoo");
        await Rename(c, 3, "Foo_z");
        await Rename(c, 4, "Foo");
        await Rename(c, 5, "afoo");
        await Rename(c, 6, "foo_a");
        await Rename(c, 7, "foo_hidden", false);
        var expected = new[] { "Foo", "foo_a", "Foo_z", "afoo", "zfoo" };
        var actual = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Search(c, new() { Q = "  fOo  ", Page = page, PageSize = 2 });
            Assert.Equal(page, result.Page);
            Assert.Equal(2, result.PageSize);
            Assert.Equal(page < 3, result.HasMore);
            actual.AddRange(result.Items.Select(i => i.Username));
            Assert.Equal(result.Items, (await Search(c, new() { Q = "FOO", Page = page, PageSize = 2 })).Items);
        }
        Assert.Equal(expected, actual);
        Assert.Empty((await Search(c, new() { Q = "foo", Page = 4, PageSize = 2 })).Items);
    }

    [Theory]
    [InlineData("a_", "a_one", "abone")]
    [InlineData("a%", "a%one", "aone")]
    [InlineData("a!", "a!one", "aone")]
    [InlineData("a!_", "a!_one", "a!xone")]
    [InlineData("a\\", "a\\one", "abone")]
    [InlineData("JOSÉ", "José", "Josue")]
    public async Task LiteralWildcardsBackslashAndAccents(string q, string match, string nonMatch)
    {
        using var c = new FriendTestContext();
        // Legacy names may contain characters no longer allowed at registration.
        await Rename(c, 2, match);
        await Rename(c, 3, nonMatch);
        Assert.Equal(match, Assert.Single((await Search(c, new() { Q = q })).Items).Username);
    }

    [Fact]
    public async Task PrefixRanking_EscapesWildcardRatherThanTreatingItAsPattern()
    {
        using var c = new FriendTestContext();
        await Rename(c, 2, "ab_x");
        await Rename(c, 3, "_x_tail");
        await Rename(c, 4, "_x");
        Assert.Equal(new[] { "_x", "_x_tail", "ab_x" },
            (await Search(c, new() { Q = "_x" })).Items.Select(i => i.Username));
    }

    [Fact]
    public async Task Relationships_AllStates_AreReadInOneStatementAndChangeImmediately()
    {
        using var c = new FriendTestContext();
        var friendship = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.AcceptAsync(2, friendship.Id));
        var sent = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(3)));
        var received = await c.Run(s => s.SendRequestAsync(4, FriendTestContext.Name(1)));
        var rejected = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(5)));
        await c.Run(s => s.RejectAsync(5, rejected.Id));
        var canceled = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(6)));
        await c.Run(s => s.CancelAsync(1, canceled.Id));
        c.Observer.Commands.Clear();
        var result = await Search(c, new() { Q = "gamer_" });
        Assert.Single(c.Observer.Commands);
        Assert.Equal(20, result.Items.Count);
        Assert.True(result.HasMore);
        var byName = result.Items.ToDictionary(i => i.Username);
        Assert.Equal(new RelationshipDto("friends"), byName[FriendTestContext.Name(2)].Relationship);
        Assert.Equal(new RelationshipDto("request_sent", sent.Id), byName[FriendTestContext.Name(3)].Relationship);
        Assert.Equal(new RelationshipDto("request_received", received.Id), byName[FriendTestContext.Name(4)].Relationship);
        foreach (var id in new[] { 5, 6, 7 }) Assert.Equal(new RelationshipDto("none"), byName[FriendTestContext.Name(id)].Relationship);
        await c.Run(s => s.RemoveFriendAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.AcceptAsync(1, received.Id));
        var changed = (await Search(c, new() { Q = "gamer_" })).Items.ToDictionary(i => i.Username);
        Assert.Equal("none", changed[FriendTestContext.Name(2)].Relationship.Status);
        Assert.Equal("friends", changed[FriendTestContext.Name(4)].Relationship.Status);
    }

    [Fact]
    public async Task Boundaries_CountUnicodeScalarsAfterTrimming()
    {
        using var c = new FriendTestContext();
        Assert.Empty((await Search(c, new() { Q = new string('z', 50) })).Items);
        Assert.Empty((await Search(c, new() { Q = string.Concat(Enumerable.Repeat("🎮", 50)) })).Items);
        await Assert.ThrowsAsync<ProfileException>(() => Search(c, new() { Q = "🎮" }));
        await Assert.ThrowsAsync<ProfileException>(() => Search(c, new() { Q = new string('z', 51) }));
    }

    [Fact]
    public void MySqlProvider_TranslatesWholeSearchIncludingRelationshipsAndStableOrdering()
    {
        using var db = new AppDbContextFactory().CreateDbContext(["--offline"]);
        var service = new UserSearchService(db, new UpperInvariantLookupNormalizer());
        var sql = service.Query(1, "A_%").Skip(20).Take(21).ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("ESCAPE '!'", sql);
        Assert.DoesNotContain("ESCAPE '\\'", sql);
        Assert.Contains("Friendships", sql);
        Assert.Contains("PendingPairKey", sql);
        Assert.Contains("EmailConfirmed", sql);
        Assert.DoesNotContain("`Email`", sql);
    }

    private static async Task Rename(FriendTestContext c, int id, string username, bool confirmed = true)
    {
        await using var db = c.CreateDb();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(update => update.SetProperty(u => u.UserName, username)
            .SetProperty(u => u.NormalizedUserName, username.ToUpperInvariant()).SetProperty(u => u.EmailConfirmed, confirmed));
    }
    private static async Task<UserSearchResponse> Search(FriendTestContext c, UserSearchRequest request)
    {
        await using var db = c.CreateDb();
        return await new UserSearchService(db, new UpperInvariantLookupNormalizer()).SearchAsync(1, request);
    }
}
