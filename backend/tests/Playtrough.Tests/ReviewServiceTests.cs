using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Services.Contracts;
using Xunit;

namespace Playtrough.Tests;

public class ReviewServiceTests
{
    [Fact]
    public async Task Create_WithoutLibraryEntry_PersistsReviewAndExactCounters()
    {
        using var context = new ReviewTestContext();
        var result = await context.UpsertAsync(4.5m, "Un juego 🎮");
        Assert.True(result.Created);
        Assert.True(result.Review.Id > 0);
        Assert.Equal(context.UserIds[0], result.Review.UserId);
        Assert.Equal(context.GameId, result.Review.GameId);
        Assert.Equal(4.5m, result.Review.Rating);
        Assert.Equal("Un juego 🎮", result.Review.Text);
        Assert.Equal(context.Clock.UtcNow.UtcDateTime, result.Review.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, result.Review.CreatedAt.Kind);
        Assert.Null(result.Review.EditedAt);
        Assert.NotEqual(Guid.Empty, result.Review.ConcurrencyToken);
        Assert.Equal(new RatingSummary(4.5m, 1), result.RatingSummary);
        var game = await context.GameAsync();
        Assert.Equal(1, game.RatingCount);
        Assert.Equal(4.5m, game.RatingSum);
        var read = await context.MineAsync();
        Assert.Equal(result.Review.ConcurrencyToken, read.Review.ConcurrencyToken);
        Assert.Equal(DateTimeKind.Utc, read.Review.CreatedAt.Kind);
        await using var db = context.CreateDb();
        Assert.Empty(await db.LibraryEntries.ToListAsync());
    }

    [Fact]
    public async Task Edit_AdjustsOnlyRatingDifference_AndPreservesCreation()
    {
        using var context = new ReviewTestContext();
        var original = await context.UpsertAsync(4.5m, "Antes");
        await context.UpsertAsync(2m, userId: context.UserIds[1]);
        context.Clock.UtcNow = context.Clock.UtcNow.AddHours(1);
        var changed = await context.UpsertAsync(1.5m, "Después");
        Assert.False(changed.Created);
        Assert.Equal(original.Review.Id, changed.Review.Id);
        Assert.Equal(original.Review.CreatedAt, changed.Review.CreatedAt);
        Assert.Equal(context.Clock.UtcNow.UtcDateTime, changed.Review.EditedAt);
        Assert.NotEqual(original.Review.ConcurrencyToken, changed.Review.ConcurrencyToken);
        Assert.Equal(new RatingSummary(1.8m, 2), changed.RatingSummary); // 1.75 rounds half up.
        var game = await context.GameAsync();
        Assert.Equal(2, game.RatingCount);
        Assert.Equal(3.5m, game.RatingSum);
        Assert.Equal(DateTimeKind.Utc, (await context.MineAsync()).Review.EditedAt!.Value.Kind);
    }

    [Fact]
    public async Task IdenticalSanitizedEdit_DoesNotWriteOrChangeAnyTimestampOrToken()
    {
        using var context = new ReviewTestContext();
        var original = await context.UpsertAsync(3.5m, "texto\nnormalizado");
        context.Clock.UtcNow = context.Clock.UtcNow.AddHours(1);
        context.Observer.Commands.Clear();
        var repeated = await context.UpsertAsync(3.5m, "  texto\r\nnormalizado\0  ");
        Assert.False(repeated.Created);
        Assert.Equal(original.Review.ConcurrencyToken, repeated.Review.ConcurrencyToken);
        Assert.Equal(original.Review.CreatedAt, repeated.Review.CreatedAt);
        Assert.Null(repeated.Review.EditedAt);
        Assert.DoesNotContain(context.Observer.Commands, sql => sql.StartsWith("UPDATE") || sql.StartsWith("INSERT"));
        var changed = await context.UpsertAsync(3.5m, "otro texto");
        var editedAt = changed.Review.EditedAt;
        context.Clock.UtcNow = context.Clock.UtcNow.AddHours(1);
        var repeatedAfterEdit = await context.UpsertAsync(3.5m, "otro texto");
        Assert.Equal(editedAt, repeatedAfterEdit.Review.EditedAt);
        Assert.Equal(changed.Review.ConcurrencyToken, repeatedAfterEdit.Review.ConcurrencyToken);
    }

    [Fact]
    public async Task TextOnlyEdit_ChangesEditedAtAndToken_WithoutWritingCounters()
    {
        using var context = new ReviewTestContext();
        var original = await context.UpsertAsync(3m);
        context.Observer.Commands.Clear();
        var changed = await context.UpsertAsync(3m, "Ahora tiene texto");
        Assert.NotNull(changed.Review.EditedAt);
        Assert.NotEqual(original.Review.ConcurrencyToken, changed.Review.ConcurrencyToken);
        Assert.Equal(original.RatingSummary, changed.RatingSummary);
        Assert.DoesNotContain(context.Observer.Commands, sql => sql.StartsWith("UPDATE \"Games\""));
    }

    [Fact]
    public async Task Delete_IsPermanent_AdjustsCounters_AndLastReviewMakesAverageNull()
    {
        using var context = new ReviewTestContext();
        await context.UpsertAsync(4.5m);
        await context.UpsertAsync(2.5m, userId: context.UserIds[1]);
        Assert.Equal(new RatingSummary(2.5m, 1), await context.DeleteAsync());
        var game = await context.GameAsync();
        Assert.Equal(1, game.RatingCount);
        Assert.Equal(2.5m, game.RatingSum);
        Assert.Equal(new RatingSummary(null, 0), await context.DeleteAsync(context.UserIds[1]));
        game = await context.GameAsync();
        Assert.Equal(0, game.RatingCount);
        Assert.Equal(0m, game.RatingSum);
        await using var db = context.CreateDb();
        Assert.Empty(await db.Reviews.ToListAsync());
        await Error("review_not_found", () => context.DeleteAsync());
        await Error("review_not_found", () => context.MineAsync());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \r\n \t ", null)]
    [InlineData("\0\u0001\u007f\u0085", null)]
    [InlineData("  uno\r\ndos\rtres\ncuatro\tfin  ", "uno\ndos\ntres\ncuatro\tfin")]
    [InlineData("  a\0\u0001b\u007fc\u0085d  ", "abcd")]
    [InlineData("  🎮 excelente 😀  ", "🎮 excelente 😀")]
    [InlineData(" <b>texto</b> & <script>ejemplo</script> ", "<b>texto</b> & <script>ejemplo</script>")]
    public async Task Text_IsTrimmedNormalizedAndStoredAsPlainText(string? input, string? expected)
    {
        using var context = new ReviewTestContext();
        Assert.Equal(expected, (await context.UpsertAsync(text: input)).Review.Text);
        Assert.Equal(expected, (await context.MineAsync()).Review.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextLimit_AppliesAfterSanitization_AndCountsUnicodeCharacters(bool emojis)
    {
        using var context = new ReviewTestContext();
        var text = string.Concat(Enumerable.Repeat(emojis ? "🎮" : "a", 2000));
        Assert.Equal(text, (await context.UpsertAsync(text: " \r\n\0" + text + "\u0001 \t")).Review.Text);
        await Error("invalid_text", () => context.UpsertAsync(text: text + "a"));
        Assert.Equal(text, (await context.MineAsync()).Review.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(1.1)]
    [InlineData(4.75)]
    public async Task InvalidRatings_AreRejectedWithoutWrites(double value)
    {
        using var context = new ReviewTestContext();
        await Error("invalid_rating", () => context.UpsertAsync((decimal)value));
        Assert.Equal(0, (await context.GameAsync()).RatingCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(2.5)]
    [InlineData(3)]
    [InlineData(3.5)]
    [InlineData(4)]
    [InlineData(4.5)]
    [InlineData(5)]
    public async Task AllHalfStarSteps_AreAccepted(double value)
    {
        using var context = new ReviewTestContext();
        Assert.Equal((decimal)value, (await context.UpsertAsync((decimal)value)).Review.Rating);
    }

    [Fact]
    public async Task MissingGame_IsGameNotFound_AndDoesNotCreateAnything()
    {
        using var context = new ReviewTestContext();
        await using var db = context.CreateDb();
        await Error("game_not_found", () => context.Service(db).UpsertAsync(context.UserIds[0], int.MaxValue, 3m, null));
        await Error("game_not_found", () => context.Service(db).GetSummaryAsync(int.MaxValue));
        Assert.Empty(await db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task UserIsolation_OnlyOwnReviewCanBeReadEditedOrDeleted()
    {
        using var context = new ReviewTestContext();
        var a = await context.UpsertAsync(5m, "A");
        await Error("review_not_found", () => context.MineAsync(context.UserIds[1]));
        await Error("review_not_found", () => context.DeleteAsync(context.UserIds[1]));
        var b = await context.UpsertAsync(1.5m, "B", context.UserIds[1]);
        Assert.True(b.Created);
        Assert.NotEqual(a.Review.Id, b.Review.Id);
        await context.UpsertAsync(2.5m, "B editada", context.UserIds[1]);
        Assert.Equal(a.Review.ConcurrencyToken, (await context.MineAsync()).Review.ConcurrencyToken);
        Assert.Equal("A", (await context.MineAsync()).Review.Text);
        await context.DeleteAsync(context.UserIds[1]);
        Assert.Equal(new RatingSummary(5m, 1), (await context.MineAsync()).RatingSummary);
    }

    [Fact]
    public async Task RandomSequences_AlwaysMatchRecalculatedReviewTotals()
    {
        using var context = new ReviewTestContext();
        var random = new Random(283031);
        var expected = new Dictionary<int, decimal>();
        for (var index = 0; index < 160; index++)
        {
            var user = context.UserIds[random.Next(context.UserIds.Length)];
            if (random.Next(3) == 0)
            {
                if (expected.Remove(user)) await context.DeleteAsync(user);
                else await Error("review_not_found", () => context.DeleteAsync(user));
            }
            else
            {
                var rating = 1m + random.Next(9) * 0.5m;
                await context.UpsertAsync(rating, random.Next(2) == 0 ? null : $"edición {index}", user);
                expected[user] = rating;
            }
            await using var db = context.CreateDb();
            var reviews = await db.Reviews.AsNoTracking().ToListAsync();
            var game = await context.GameAsync();
            Assert.Equal(expected.Count, reviews.Count);
            Assert.Equal(reviews.Count, game.RatingCount);
            Assert.Equal(reviews.Sum(review => review.Rating), game.RatingSum);
            Assert.Equal(expected.Values.Sum(), game.RatingSum);
            Assert.True(game.RatingCount >= 0 && game.RatingSum >= 0m);
            Assert.Equal(RatingSummary.FromTotals(reviews.Count, reviews.Sum(review => review.Rating)),
                await context.Service(db).GetSummaryAsync(context.GameId));
        }
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(2, 7.5, 3.8)]
    [InlineData(4, 16.5, 4.1)]
    [InlineData(3, 10, 3.3)]
    public void Summary_UsesDecimalHalfUpRounding(int count, double sum, double? average)
        => Assert.Equal(average is null ? (decimal?)null : (decimal)average, RatingSummary.FromTotals(count, (decimal)sum).Average);

    private static async Task Error(string code, Func<Task> action)
        => Assert.Equal(code, (await Assert.ThrowsAsync<ReviewException>(action)).Code);
}
