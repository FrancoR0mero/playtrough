using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Tests;

internal sealed class TimelineReadTestContext : IDisposable
{
    public ReviewTestContext Store { get; } = new();
    public TimelineSettings Settings { get; } = new();
    public TimelineCursorCodec Cursors { get; } = new(new EphemeralDataProtectionProvider());
    public int Owner => Store.UserIds[0];
    public int Friend => Store.UserIds[1];
    public int Stranger => Store.UserIds[2];
    public TimelineReadTestContext()
    {
        using var db = Store.CreateDb(false);
        db.Users.ExecuteUpdate(u => u.SetProperty(user => user.EmailConfirmed, true));
    }
    public TimelineQuery Query(AppDbContext db) => new(db, Options.Create(Settings), Store.Clock);
    public TimelineService Service(AppDbContext db) => new(db, new UpperInvariantLookupNormalizer(), new PrivacyService(db), Query(db), Cursors, new(db));
    public async Task<TimelineEntry> AddAsync(string date = "2026-10-30", DateTime? recorded = null,
        int? owner = null, bool hidden = false, LibraryEventType type = LibraryEventType.Added)
    {
        await using var db = Store.CreateDb(false);
        var change = new LibraryEvent
        {
            UserId = owner ?? Owner, GameId = Store.GameId, Type = type,
            FromStatus = type == LibraryEventType.StatusChanged ? LibraryStatus.Playing : null,
            ToStatus = type == LibraryEventType.StatusChanged ? LibraryStatus.Completed : LibraryStatus.Abandoned,
            OccurredOn = DateOnly.Parse(date), RecordedAt = recorded ?? Store.Clock.UtcNow.UtcDateTime,
        };
        var entry = new TimelineWriter(db, Options.Create(new FeedSettings())).AddLibraryEvent(change)!;
        entry.IsHidden = hidden;
        await db.SaveChangesAsync();
        return entry;
    }
    public async Task<TimelinePageDto> ListAsync(TimelineListRequest? request = null, int? viewer = null, string? username = null)
    {
        await using var db = Store.CreateDb();
        return await Service(db).ListAsync(viewer ?? Owner, username, request ?? new());
    }
    public async Task<TimelinePeriodsDto> PeriodsAsync(int? viewer = null, string? username = null, string? type = null)
    {
        await using var db = Store.CreateDb();
        return await Service(db).PeriodsAsync(viewer ?? Owner, username, type);
    }
    public void Dispose() => Store.Dispose();
}
