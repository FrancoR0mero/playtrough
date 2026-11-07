using Playtrough.Api.Dtos.Reviews;
using Xunit;

namespace Playtrough.Tests;

public class ReviewListQueryTests
{
    [Theory]
    [InlineData("recent")]
    [InlineData("top")]
    [InlineData("low")]
    public async Task ListSql_OnlyProjectsRequiredAuthorData_AndDoesNotTrackEntities(string sort)
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(4.5m);
        await context.UpsertAsync(2.5m, userId: context.UserIds[1]);
        context.Observer.Commands.Clear();
        await using var db = context.CreateDb();
        var result = await context.Service(db).ListAsync(context.UserIds[0], context.GameId,
            new ReviewListRequest { Sort = sort, PageSize = 1 });
        Assert.Single(result.Items);
        Assert.Equal(2, result.RatingSummary.Count);
        Assert.True(result.HasMore);
        Assert.Empty(db.ChangeTracker.Entries());
        var sql = Assert.Single(context.Observer.Commands, command => command.Contains("JOIN \"Users\""));
        Assert.Contains("\"UserName\"", sql);
        Assert.Contains("LIMIT", sql);
        foreach (var field in new[] { "Email", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "SecurityStamp", "ConcurrencyToken" })
            Assert.DoesNotContain($"\"{field}\"", sql);
    }

    [Fact]
    public async Task RetryingExecutionStrategy_CanReplayTheConsistentReadTransaction()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(4.5m);
        var attempts = 0;
        context.Observer.BeforeSummaryRead = _ =>
        {
            if (++attempts == 1) throw new ReviewTransientException();
            return Task.CompletedTask;
        };
        await using var db = context.CreateDb(retry: true);
        var result = await context.Service(db).ListAsync(context.UserIds[0], context.GameId, new ReviewListRequest());
        Assert.Equal(2, attempts);
        Assert.Single(result.Items);
        Assert.Equal(4.5m, result.RatingSummary.Average);
        Assert.Equal(1, result.RatingSummary.Count);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
    }
}
