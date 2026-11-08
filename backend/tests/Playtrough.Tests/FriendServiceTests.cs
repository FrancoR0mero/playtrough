using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Events;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class FriendServiceTests
{
    [Fact]
    public async Task Send_ValidatesSelfMissingUnconfirmedAndBothPendingDirections()
    {
        using var c = new FriendTestContext();
        await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(1))), "cannot_befriend_self");
        await Error(c.Run(s => s.SendRequestAsync(1, "missing")), "user_not_found");
        await using (var db = c.CreateDb())
            await db.Users.Where(u => u.Id == 3).ExecuteUpdateAsync(update => update.SetProperty(u => u.EmailConfirmed, false));
        await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(3))), "user_not_found");
        var request = await c.Run(s => s.SendRequestAsync(1, "GAMER_02"));
        Assert.Equal("pending", request.Status);
        Assert.Equal(c.Clock.UtcNow.UtcDateTime, request.CreatedAt);
        Assert.Equal(new FriendsSummaryDto(0, 0, 1), await c.Run(s => s.SummaryAsync(1)));
        Assert.Equal(new FriendsSummaryDto(0, 1, 0), await c.Run(s => s.SummaryAsync(2)));
        await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2))), "request_already_sent");
        var reversed = await Error(c.Run(s => s.SendRequestAsync(2, FriendTestContext.Name(1))), "request_already_received");
        Assert.Equal(request.Id, reversed.RequestId);
        Assert.Single(c.Events.Published.OfType<FriendRequestSent>());
        Assert.Equal("request_sent", (await c.Run(s => s.RelationshipAsync(1, 2))).Status);
        Assert.Equal("request_received", (await c.Run(s => s.RelationshipAsync(2, 1))).Status);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(2)]
    public async Task Rejection_IsSilentIdempotentAndDirectional_WithExactCooldown(int days)
    {
        using var c = new FriendTestContext();
        c.Settings.RejectCooldownDays = days;
        var first = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.RejectAsync(2, first.Id));
        var available = c.Clock.UtcNow.UtcDateTime.AddDays(days);
        c.Clock.UtcNow = c.Clock.UtcNow.AddDays(1);
        await c.Run(s => s.RejectAsync(2, first.Id)); // Does not extend cooldown.
        Assert.Single(c.Events.Published);
        var blocked = await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2))), "cooldown_active");
        Assert.Equal(available, blocked.AvailableAt);
        var reverse = await c.Run(s => s.SendRequestAsync(2, FriendTestContext.Name(1)));
        await c.Run(s => s.CancelAsync(2, reverse.Id));
        c.Clock.UtcNow = new DateTimeOffset(available.AddTicks(-1));
        await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2))), "cooldown_active");
        c.Clock.UtcNow = new DateTimeOffset(available);
        var second = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        Assert.NotEqual(first.Id, second.Id);
        await c.Run(s => s.RejectAsync(2, second.Id));
        Assert.Equal(available.AddDays(days), (await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2))), "cooldown_active")).AvailableAt);
    }

    [Fact]
    public async Task DefaultQuota50_AndConfiguredQuota_AreEnforced()
    {
        using var c = new FriendTestContext();
        for (var i = 2; i <= 51; i++) await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(i)));
        await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(52))), "too_many_pending_requests");
        Assert.Equal(50, (await c.Run(s => s.SummaryAsync(1))).PendingSentCount);
        c.Settings.MaxPendingSent = 1;
        await c.Run(s => s.SendRequestAsync(55, FriendTestContext.Name(56)));
        await Error(c.Run(s => s.SendRequestAsync(55, FriendTestContext.Name(57))), "too_many_pending_requests");
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    [InlineData("cancel")]
    public async Task RequestWrites_HideExistenceFromWrongParticipantAndStrangers(string operation)
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        foreach (var user in new[] { operation == "cancel" ? 2 : 1, 3 })
            await Error(Act(c, operation, user, request.Id), "request_not_found");
        await Error(Act(c, operation, operation == "cancel" ? 1 : 2, int.MaxValue), "request_not_found");
        Assert.Equal("request_sent", (await c.Run(s => s.RelationshipAsync(1, 2))).Status);
    }

    [Theory]
    [InlineData("accept", "reject")]
    [InlineData("accept", "cancel")]
    [InlineData("reject", "accept")]
    [InlineData("reject", "cancel")]
    [InlineData("cancel", "accept")]
    [InlineData("cancel", "reject")]
    public async Task TerminalStates_RejectOtherTransitions(string first, string second)
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        await Act(c, first, first == "cancel" ? 1 : 2, request.Id);
        await Error(Act(c, second, second == "cancel" ? 1 : 2, request.Id), "request_not_pending");
    }

    [Fact]
    public async Task Accept_CanonicalAndIdempotent_RemoveIdempotentAndNoCooldown()
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(2, FriendTestContext.Name(1)));
        var accepted = await c.Run(s => s.AcceptAsync(1, request.Id));
        Assert.Equal(accepted, await c.Run(s => s.AcceptAsync(1, request.Id)));
        Assert.Single(c.Events.Published.OfType<FriendRequestAccepted>());
        await using (var db = c.CreateDb())
        {
            var friendship = Assert.Single(await db.Friendships.ToListAsync());
            Assert.Equal((1, 2), (friendship.UserLowId, friendship.UserHighId));
            var row = await db.FriendRequests.SingleAsync();
            Assert.Equal(FriendRequestStatus.Accepted, row.Status);
            Assert.Equal(accepted.Since, row.RespondedAt);
            Assert.Null(row.PendingPairKey);
        }
        Assert.Equal(new FriendsSummaryDto(1, 0, 0), await c.Run(s => s.SummaryAsync(1)));
        Assert.Equal("friends", (await c.Run(s => s.RelationshipAsync(2, 1))).Status);
        await Error(c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2))), "already_friends");
        var other = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(3)));
        await c.Run(s => s.AcceptAsync(3, other.Id));
        await c.Run(s => s.RemoveFriendAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.RemoveFriendAsync(2, FriendTestContext.Name(1)));
        Assert.Equal("friends", (await c.Run(s => s.RelationshipAsync(1, 3))).Status);
        Assert.Equal("none", (await c.Run(s => s.RelationshipAsync(1, 2))).Status);
        // Accepting old history must not recreate a removed friendship.
        Assert.Equal(accepted, await c.Run(s => s.AcceptAsync(1, request.Id)));
        Assert.Equal("none", (await c.Run(s => s.RelationshipAsync(1, 2))).Status);
        await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
    }

    [Fact]
    public async Task Cancel_Idempotent_NoCooldown_NoEvents_AndFreesQuota()
    {
        using var c = new FriendTestContext();
        c.Settings.MaxPendingSent = 1;
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        await c.Run(s => s.CancelAsync(1, request.Id));
        c.Clock.UtcNow = c.Clock.UtcNow.AddDays(1);
        await c.Run(s => s.CancelAsync(1, request.Id));
        await using (var db = c.CreateDb())
            Assert.Equal(request.CreatedAt, (await db.FriendRequests.SingleAsync()).RespondedAt);
        Assert.Single(c.Events.Published);
        Assert.Equal(new FriendsSummaryDto(0, 0, 0), await c.Run(s => s.SummaryAsync(1)));
        Assert.Equal(new FriendsSummaryDto(0, 0, 0), await c.Run(s => s.SummaryAsync(2)));
        await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
    }

    [Fact]
    public async Task FailedFriendshipInsert_RollsBackAcceptanceAndPublishesNothing()
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        c.Observer.FailFriendshipInsert = true;
        await Assert.ThrowsAnyAsync<Exception>(() => c.Run(s => s.AcceptAsync(2, request.Id)));
        await using var db = c.CreateDb(false);
        var row = await db.FriendRequests.SingleAsync();
        Assert.Equal(FriendRequestStatus.Pending, row.Status);
        Assert.Null(row.RespondedAt);
        Assert.NotNull(row.PendingPairKey);
        Assert.Empty(await db.Friendships.ToListAsync());
        Assert.Empty(c.Events.Published.OfType<FriendRequestAccepted>());
        c.Observer.FailFriendshipInsert = false;
        await c.Run(s => s.AcceptAsync(2, request.Id));
        Assert.Single(c.Events.Published.OfType<FriendRequestAccepted>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSends_OnePendingAndOneDomainConflict(bool reverse)
    {
        using var c = new FriendTestContext();
        c.SynchronizeInitialReads();
        var errors = await Task.WhenAll(Task.Run(() => Record.ExceptionAsync(() => c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2))))),
            Task.Run(() => Record.ExceptionAsync(() => c.Run(s => s.SendRequestAsync(reverse ? 2 : 1, FriendTestContext.Name(reverse ? 1 : 2))))));
        Assert.Single(errors, e => e is null);
        var failure = Assert.IsType<FriendException>(Assert.Single(errors, e => e is not null));
        Assert.Equal(reverse ? "request_already_received" : "request_already_sent", failure.Code);
        await using var db = c.CreateDb(false);
        Assert.Single(await db.FriendRequests.Where(r => r.Status == FriendRequestStatus.Pending).ToListAsync());
        Assert.Single(c.Events.Published.OfType<FriendRequestSent>());
    }

    [Fact]
    public async Task ConcurrentSends_DifferentPairs_CannotExceedSenderQuota()
    {
        using var c = new FriendTestContext();
        c.Settings.MaxPendingSent = 1;
        c.SynchronizeInitialReads();
        var errors = await Task.WhenAll(new[] { 2, 3 }.Select(id => Task.Run(() =>
            Record.ExceptionAsync(() => c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(id)))))));
        Assert.Single(errors, e => e is null);
        Assert.Equal("too_many_pending_requests", Assert.IsType<FriendException>(Assert.Single(errors, e => e is not null)).Code);
        Assert.Equal(1, (await c.Run(s => s.SummaryAsync(1))).PendingSentCount);
    }

    [Fact]
    public async Task ConcurrentAccepts_CreateOneFriendshipAndPublishExactlyOnce()
    {
        using var c = new FriendTestContext();
        var request = await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(2)));
        c.SynchronizeInitialReads();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => c.Run(s => s.AcceptAsync(2, request.Id)))));
        Assert.Equal(results[0], results[1]);
        await using var db = c.CreateDb(false);
        Assert.Single(await db.Friendships.ToListAsync());
        Assert.Single(c.Events.Published.OfType<FriendRequestAccepted>());
    }

    [Fact]
    public async Task Lists_StablePagesLiteralSearchAndIsolation_SummaryAndOneQueryRelationship()
    {
        using var c = new FriendTestContext();
        var ids = new List<int>();
        for (var other = 2; other <= 8; other++)
            ids.Add((await c.Run(s => s.SendRequestAsync(1, FriendTestContext.Name(other)))).Id);
        var sent = new List<int>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await c.Run(s => s.RequestsAsync(1, false, new() { Page = page, PageSize = 2 }));
            Assert.Equal(page < 4, result.HasMore);
            sent.AddRange(result.Items.Select(r => r.Id));
        }
        Assert.Equal(ids.AsEnumerable().Reverse(), sent);
        Assert.Empty((await c.Run(s => s.RequestsAsync(9, false, new()))).Items);
        Assert.Empty((await c.Run(s => s.RequestsAsync(9, true, new()))).Items);
        Assert.Single((await c.Run(s => s.RequestsAsync(2, true, new()))).Items);
        for (var i = 0; i < ids.Count; i++) await c.Run(s => s.AcceptAsync(i + 2, ids[i]));
        var names = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await c.Run(s => s.ListAsync(1, null, new() { Page = page, PageSize = 2 }));
            Assert.Equal(page < 4, result.HasMore);
            names.AddRange(result.Items.Select(f => f.User.Username));
            Assert.All(result.Items, f => Assert.Equal(c.Clock.UtcNow.UtcDateTime, f.Since));
        }
        Assert.Equal(Enumerable.Range(2, 7).Select(FriendTestContext.Name), names);
        Assert.Single((await c.Run(s => s.ListAsync(1, " GAMER_02 ", new()))).Items);
        Assert.Empty((await c.Run(s => s.ListAsync(1, "%", new()))).Items);
        Assert.Empty((await c.Run(s => s.ListAsync(9, null, new()))).Items);
        Assert.Equal(new FriendsSummaryDto(7, 0, 0), await c.Run(s => s.SummaryAsync(1)));
        c.Observer.Commands.Clear();
        Assert.Equal("friends", (await c.Run(s => s.RelationshipAsync(1, 2))).Status);
        Assert.Single(c.Observer.Commands);
    }

    private static Task Act(FriendTestContext c, string op, int user, int id) => op switch
    {
        "accept" => c.Run(s => s.AcceptAsync(user, id)),
        "reject" => c.Run(s => s.RejectAsync(user, id)),
        _ => c.Run(s => s.CancelAsync(user, id)),
    };
    private static async Task<FriendException> Error(Task action, string code)
    {
        var error = await Assert.ThrowsAsync<FriendException>(() => action);
        Assert.Equal(code, error.Code);
        return error;
    }
}
