using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Implementations;
using Xunit;

namespace Playtrough.Tests;

public class TimelineReadTests
{
    [Fact]
    public async Task Order_UsesEffectiveDateThenRecordedAtThenId_AndIsolatesOwners()
    {
        using var c = new TimelineReadTestContext();
        var newest = await c.AddAsync("2026-10-30");
        var retro = await c.AddAsync("2020-01-01", c.Store.Clock.UtcNow.AddDays(1).UtcDateTime);
        var tie = await c.AddAsync("2026-10-30");
        var recorded = await c.AddAsync("2026-10-30", c.Store.Clock.UtcNow.AddHours(1).UtcDateTime);
        await c.AddAsync("2026-11-01", owner: c.Stranger);
        var page = await c.ListAsync();
        Assert.Equal(new[] { recorded.Id, tie.Id, newest.Id, retro.Id }, page.Items.Select(e => e.Id));
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task KeysetPagination_DoesNotRepeatOrOmitEntriesWhenNewerFactsArrive()
    {
        using var c = new TimelineReadTestContext();
        var ids = new List<long>();
        for (var i = 0; i < 8; i++) ids.Add((await c.AddAsync(recorded: c.Store.Clock.UtcNow.AddMinutes(i).UtcDateTime)).Id);
        var first = await c.ListAsync(new() { Limit = 2 });
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);
        await c.AddAsync("2026-11-01");
        var seen = first.Items.Select(e => e.Id).ToList();
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var next = await c.ListAsync(new() { Limit = 2, Cursor = cursor });
            seen.AddRange(next.Items.Select(e => e.Id));
            Assert.Equal(next.HasMore, next.NextCursor is not null);
            cursor = next.NextCursor;
        }
        Assert.Equal(ids.AsEnumerable().Reverse(), seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Theory]
    [InlineData(2025, null, null, 2)]
    [InlineData(2025, 2, null, 1)]
    [InlineData(null, null, "status_changed", 1)]
    [InlineData(2025, null, "added,status_changed", 2)]
    [InlineData(2025, 2, "added", 0)]
    [InlineData(null, 1, "added,status_changed", 2)]
    [InlineData(null, 2, "added,status_changed", 1)]
    public async Task DateAndTypeFilters_Combine(int? year, int? month, string? type, int count)
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync("2025-01-01");
        await c.AddAsync("2025-02-01", type: LibraryEventType.StatusChanged);
        await c.AddAsync("2026-01-01");
        var result = await c.ListAsync(new() { Year = year, Month = month, Type = type });
        Assert.Equal(count, result.Items.Count);
    }

    [Fact]
    public async Task MonthWithoutYear_PaginatesAcrossYearsWithExistingVisibilityRules()
    {
        using var c = new TimelineReadTestContext();
        var older = await c.AddAsync("2025-01-01");
        var newer = await c.AddAsync("2026-01-15", type: LibraryEventType.StatusChanged);
        await c.AddAsync("2026-01-31", hidden: true);
        await c.AddAsync("2026-02-01");

        var first = await c.ListAsync(new() { Month = 1, Limit = 1, Type = "added,status_changed" }, viewer: c.Friend, username: "review-1");
        Assert.Equal(newer.Id, Assert.Single(first.Items).Id);
        Assert.True(first.HasMore);
        var second = await c.ListAsync(new() { Month = 1, Limit = 1, Type = "added,status_changed", Cursor = first.NextCursor }, viewer: c.Friend, username: "review-1");
        Assert.Equal(older.Id, Assert.Single(second.Items).Id);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task Cursor_IsAuthenticatedAndBoundToFiltersOwnerViewerAndVersion()
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync(); await c.AddAsync();
        var cursor = (await c.ListAsync(new() { Limit = 1 })).NextCursor!;
        foreach (var invalid in new[] { "", "garbage", "v2." + cursor[3..], cursor + "=", cursor[..20] + (cursor[20] == 'a' ? 'b' : 'a') + cursor[21..] })
            await Error("invalid_cursor", () => c.ListAsync(new() { Cursor = invalid }));
        await Error("invalid_cursor", () => c.ListAsync(new() { Cursor = cursor, Year = 2026 }));
        await Error("invalid_cursor", () => c.ListAsync(new() { Cursor = cursor }, viewer: c.Friend, username: "review-1"));
        await Error("invalid_cursor", () => c.ListAsync(new() { Cursor = cursor }, username: "review-2"));
        var changedLimit = await c.ListAsync(new() { Cursor = cursor, Limit = 5 });
        Assert.Single(changedLimit.Items);
    }

    [Fact]
    public async Task Dto_ProjectsGameStatusesAndCurrentReview_WithoutDuplicatingOnEdit()
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync();
        await c.AddAsync(type: LibraryEventType.StatusChanged);
        await c.Store.UpsertAsync(3, "initial text");
        await using (var db = c.Store.CreateDb(false))
            await db.Games.ExecuteUpdateAsync(u => u.SetProperty(g => g.GameBrainId, 12345)
                .SetProperty(g => g.Name, "Example").SetProperty(g => g.Slug, "example")
                .SetProperty(g => g.CoverImageUrl, "https://images.example/cover.png")
                .SetProperty(g => g.FirstReleaseDate, new DateOnly(2020, 2, 29)));
        var before = await c.ListAsync();
        var reviewed = Assert.Single(before.Items, e => e.Type == "reviewed");
        Assert.Equal(3, reviewed.Review!.Rating);
        Assert.Equal("initial text", reviewed.Review.Excerpt);
        Assert.Null(reviewed.FromStatus); Assert.Null(reviewed.ToStatus);
        var added = Assert.Single(before.Items, e => e.Type == "added");
        Assert.Null(added.FromStatus); Assert.Equal("abandoned", added.ToStatus); Assert.Null(added.Review);
        var changed = Assert.Single(before.Items, e => e.Type == "status_changed");
        Assert.Equal("playing", changed.FromStatus); Assert.Equal("completed", changed.ToStatus);
        Assert.All(before.Items, e => Assert.Equal(new TimelineGameDto(c.Store.GameId, 12345, "Example", "example", "https://images.example/cover.png", 2020), e.Game));
        await c.Store.UpsertAsync(5, null);
        var after = await c.ListAsync();
        var edited = Assert.Single(after.Items, e => e.Type == "reviewed");
        Assert.Equal(reviewed.Id, edited.Id);
        Assert.Equal(5, edited.Review!.Rating);
        Assert.Null(edited.Review.Excerpt);
        Assert.Equal(3, after.Items.Count);
    }

    [Fact]
    public async Task Periods_AggregateEffectiveDatesAndRespectHiddenEntriesAndTypes()
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync("2025-01-01");
        await c.AddAsync("2025-02-01", type: LibraryEventType.StatusChanged);
        await c.AddAsync("2026-01-01", hidden: true);
        var own = await c.PeriodsAsync();
        Assert.Equal(new[] { 2026, 2025 }, own.Years.Select(y => y.Year));
        Assert.Equal(2, own.Years[1].Count);
        Assert.Equal(new[] { 2, 1 }, own.Years[1].Months.Select(m => m.Month));
        var other = await c.PeriodsAsync(c.Stranger, "review-1");
        Assert.Equal(2025, Assert.Single(other.Years).Year);
        Assert.Equal(2, (await c.ListAsync(viewer: c.Stranger, username: "review-1")).Items.Count);
        var typed = await c.PeriodsAsync(type: "status_changed");
        Assert.Equal(2, Assert.Single(Assert.Single(typed.Years).Months).Month);
        Assert.Equal(1, typed.Years[0].Count);
        Assert.All((await c.ListAsync(viewer: c.Stranger, username: "review-1")).Items, e => Assert.False(e.IsHidden));
        Assert.True(Assert.Single((await c.ListAsync()).Items, e => e.OccurredOn.Year == 2026).IsHidden);
    }

    [Fact]
    public async Task Queries_HaveConstantSqlCount_ProjectOnlyNeededColumns_AndDoNotTrack()
    {
        using var c = new TimelineReadTestContext();
        await c.AddAsync();
        await using var db = c.Store.CreateDb();
        c.Store.Observer.Commands.Clear();
        await c.Service(db).ListAsync(c.Owner, null, new());
        var one = c.Store.Observer.Commands.Count;
        Assert.Equal(1, one);
        for (var i = 0; i < 12; i++) await c.AddAsync();
        c.Store.Observer.Commands.Clear();
        await c.Service(db).ListAsync(c.Owner, null, new() { Limit = 50 });
        Assert.Equal(one, c.Store.Observer.Commands.Count);
        var sql = Assert.Single(c.Store.Observer.Commands);
        Assert.Contains("JOIN", sql); Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("ProviderDetailsJson", sql); Assert.DoesNotContain("Email", sql);
        c.Store.Observer.Commands.Clear();
        await c.Service(db).PeriodsAsync(c.Owner, null, null);
        Assert.Single(c.Store.Observer.Commands);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(null, 240, null)]
    [InlineData("   ", 240, null)]
    [InlineData("one two three", 10, "one two…")]
    [InlineData("one two three", 8, "one two…")]
    [InlineData("one two three", 3, "…")]
    [InlineData("hello", 5, "hello")]
    [InlineData("😀 😀 😀", 4, "😀 😀…")]
    public void Excerpts_RespectWordBoundariesAndUnicodeLength(string? text, int limit, string? expected)
        => Assert.Equal(expected, TimelineQuery.Excerpt(text, limit));

    [Fact]
    public async Task ConfiguredLimitAndExcerptLength_AreUsedByTheReadEndpoints()
    {
        using var c = new TimelineReadTestContext();
        c.Settings.DefaultLimit = 2; c.Settings.MaxLimit = 3; c.Settings.ExcerptLength = 10;
        await c.AddAsync(); await c.AddAsync();
        await c.Store.UpsertAsync(4, "one two three four");
        var page = await c.ListAsync();
        Assert.Equal(2, page.Items.Count); Assert.True(page.HasMore);
        await TimelineReadTests.Error("invalid_filter", () => c.ListAsync(new() { Limit = 4 }));
        var reviewed = await c.ListAsync(new() { Type = "reviewed" });
        Assert.Equal("one two…", Assert.Single(reviewed.Items).Review!.Excerpt);
    }

    internal static async Task Error(string code, Func<Task> action)
        => Assert.Equal(code, (await Assert.ThrowsAsync<TimelineException>(action)).Code);
}
