using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Timeline;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;

namespace Playtrough.Api.Services.Implementations;

public sealed record TimelineFilters(int? Year, int? Month, TimelineEntryType[] Types)
{
    public string Fingerprint => FormattableString.Invariant($"{Year ?? 0}:{Month ?? 0}:{string.Join(',', Types.Select(t => (int)t))}");
}
public sealed record FeedPosition(DateTime RecordedAt, long Id);

public sealed class TimelineQuery(AppDbContext db, IOptions<TimelineSettings> settings, TimeProvider time)
{
    internal IQueryable<FeedEntryDto> FeedEntries(int viewerId, FeedPosition? position, int limit)
    {
        // Canonical friendships make the two directions disjoint, so UNION ALL
        // needs no deduplication and both indexed access paths remain available.
        var friends = db.Friendships.Where(f => f.UserLowId == viewerId).Select(f => f.UserHighId)
            .Concat(db.Friendships.Where(f => f.UserHighId == viewerId).Select(f => f.UserLowId));
        // Parameterized equality lets MySQL seek the leading FeedEligible key;
        // a bare boolean predicate is emitted as <> 0 and can lose that access path.
        var eligible = true;
        var source = from entry in db.TimelineEntries.AsNoTracking()
                     join friendId in friends on entry.UserId equals friendId
                     where entry.FeedEligible == eligible
                     select entry;
        var entries = ApplyFilters(source, null, false, new(null, null, []));
        if (position is { } cursor)
            entries = entries.Where(e => e.RecordedAt < cursor.RecordedAt || e.RecordedAt == cursor.RecordedAt && e.Id < cursor.Id);
        var excerptLength = settings.Value.ExcerptLength;
        // Every confirmed friend satisfies the unified Public/Private policy.
        // Blocks still deny access in either direction, even with a stale friendship.
        return (from entry in entries
                join author in db.Users.AsNoTracking() on entry.UserId equals author.Id
                join game in db.Games.AsNoTracking() on entry.GameId equals game.Id
                join review in db.Reviews.AsNoTracking() on entry.ReviewId equals (int?)review.Id into reviews
                from review in reviews.DefaultIfEmpty()
                join mine in ViewerReactions(viewerId) on entry.Id equals mine.EntryId into reactions
                from mine in reactions.DefaultIfEmpty()
                where author.EmailConfirmed && !db.UserBlocks.Any(b =>
                    b.BlockerId == viewerId && b.BlockedId == author.Id || b.BlockerId == author.Id && b.BlockedId == viewerId)
                orderby entry.RecordedAt descending, entry.Id descending
                select new FeedEntryDto(entry.Id, TimelineValues.TypeValue(entry.Type), entry.OccurredOn, entry.RecordedAt,
                    new TimelineGameDto(game.Id, game.GameBrainId ?? game.ExternalId, game.Name, game.Slug,
                        game.CoverImageUrl, game.FirstReleaseDate.HasValue ? game.FirstReleaseDate.GetValueOrDefault().Year : null),
                    entry.FromStatus.HasValue ? LibraryValues.StatusValue(entry.FromStatus.GetValueOrDefault()) : null,
                    entry.ToStatus.HasValue ? LibraryValues.StatusValue(entry.ToStatus.GetValueOrDefault()) : null,
                    entry.Type == TimelineEntryType.Reviewed && review != null
                        ? new TimelineReviewDto(review.Id, review.Rating, Excerpt(review.Text, excerptLength)) : null,
                    false, new FeedAuthorDto(author.UserName!, AvatarUrls.FromFile(author.AvatarFileName)))
                {
                    LikeCount = entry.LikeCount, DislikeCount = entry.DislikeCount, CommentCount = entry.CommentCount,
                    MyReaction = ReactionService.Name((ReactionValue?)mine.Value),
                })
            .Take(limit + 1);
    }

    public Task<List<FeedEntryDto>> FeedAsync(int viewerId, FeedPosition? position, int limit, CancellationToken ct)
        => FeedEntries(viewerId, position, limit).ToListAsync(ct);

    public TimelineFilters Filters(int? year, int? month, string? type)
    {
        if (year is { } y && (y < 1970 || y > Math.Min(9999, time.GetUtcNow().Year + 1))
            || month is { } m && m is < 1 or > 12) throw new TimelineException("invalid_filter");
        var types = new List<TimelineEntryType>();
        if (type is not null)
            foreach (var part in type.Split(','))
                types.Add(part.Trim() switch
                {
                    "added" => TimelineEntryType.Added,
                    "status_changed" => TimelineEntryType.StatusChanged,
                    "reviewed" => TimelineEntryType.Reviewed,
                    _ => throw new TimelineException("invalid_filter"),
                });
        return new(year, month, types.Distinct().Order().ToArray());
    }

    public int Limit(int? requested)
    {
        var value = requested ?? settings.Value.DefaultLimit;
        if (value < 1 || value > settings.Value.MaxLimit) throw new TimelineException("invalid_filter");
        return value;
    }

    // All visibility/date/type/keyset predicates live here. A feed can supply a
    // friendship-filtered source without duplicating the hidden-entry predicate.
    internal IQueryable<TimelineEntry> ApplyFilters(IQueryable<TimelineEntry> source, int? ownerId,
        bool includeHidden, TimelineFilters filters, TimelinePosition? position = null)
    {
        if (ownerId is { } owner) source = source.Where(e => e.UserId == owner);
        if (!includeHidden) source = source.Where(e => !e.IsHidden);
        if (filters.Year is { } year)
        {
            var start = new DateOnly(year, filters.Month ?? 1, 1);
            var end = filters.Month is { } month ? new DateOnly(year, month, DateTime.DaysInMonth(year, month)) : new DateOnly(year, 12, 31);
            source = source.Where(e => e.OccurredOn >= start && e.OccurredOn <= end);
        }
        else if (filters.Month is { } month)
        {
            source = source.Where(e => e.OccurredOn.Month == month);
        }
        if (filters.Types.Length > 0) source = source.Where(e => filters.Types.Contains(e.Type));
        if (position is { } cursor)
            source = source.Where(e => e.OccurredOn < cursor.OccurredOn
                || e.OccurredOn == cursor.OccurredOn && (e.RecordedAt < cursor.RecordedAt
                    || e.RecordedAt == cursor.RecordedAt && e.Id < cursor.Id));
        return source;
    }

    public Task<List<TimelineEntryDto>> ListAsync(int ownerId, bool includeHidden, TimelineFilters filters,
        TimelinePosition? position, int limit, int viewerId, CancellationToken ct)
        => Entries(ownerId, includeHidden, filters, position, limit, viewerId).ToListAsync(ct);

    internal IQueryable<TimelineEntryDto> Entries(int ownerId, bool includeHidden, TimelineFilters filters,
        TimelinePosition? position, int limit, int viewerId)
    {
        var entries = ApplyFilters(db.TimelineEntries.AsNoTracking(), ownerId, includeHidden, filters, position)
            .OrderByDescending(e => e.OccurredOn).ThenByDescending(e => e.RecordedAt).ThenByDescending(e => e.Id)
            .Take(limit + 1);
        return Project(entries, includeHidden, viewerId);
    }

    public Task<TimelineEntryDto?> EntryAsync(int ownerId, long id, CancellationToken ct)
        => Project(ApplyFilters(db.TimelineEntries.AsNoTracking(), ownerId, true, new(null, null, []))
            .Where(e => e.Id == id), true, ownerId).SingleOrDefaultAsync(ct);

    private IQueryable<TimelineEntryReaction> ViewerReactions(int viewerId)
        => db.TimelineEntryReactions.AsNoTracking().Where(reaction => reaction.UserId == viewerId);

    private IQueryable<TimelineEntryDto> Project(IQueryable<TimelineEntry> entries, bool includeHidden, int viewerId)
    {
        var excerptLength = settings.Value.ExcerptLength;
        return (from e in entries
                join game in db.Games.AsNoTracking() on e.GameId equals game.Id
                join review in db.Reviews.AsNoTracking() on e.ReviewId equals (int?)review.Id into reviews
                from review in reviews.DefaultIfEmpty()
                join mine in ViewerReactions(viewerId) on e.Id equals mine.EntryId into reactions
                from mine in reactions.DefaultIfEmpty()
                orderby e.OccurredOn descending, e.RecordedAt descending, e.Id descending
                select new TimelineEntryDto(e.Id, TimelineValues.TypeValue(e.Type), e.OccurredOn, e.RecordedAt,
                    new TimelineGameDto(game.Id, game.GameBrainId ?? game.ExternalId, game.Name, game.Slug,
                        game.CoverImageUrl, game.FirstReleaseDate.HasValue ? game.FirstReleaseDate.GetValueOrDefault().Year : null),
                    e.FromStatus.HasValue ? LibraryValues.StatusValue(e.FromStatus.GetValueOrDefault()) : null,
                    e.ToStatus.HasValue ? LibraryValues.StatusValue(e.ToStatus.GetValueOrDefault()) : null,
                    e.Type == TimelineEntryType.Reviewed && review != null
                        ? new TimelineReviewDto(review.Id, review.Rating, Excerpt(review.Text, excerptLength)) : null,
                    includeHidden && e.IsHidden)
                {
                    LikeCount = e.LikeCount, DislikeCount = e.DislikeCount, CommentCount = e.CommentCount,
                    MyReaction = ReactionService.Name((ReactionValue?)mine.Value),
                });
    }

    public async Task<TimelinePeriodsDto> PeriodsAsync(int ownerId, bool includeHidden, TimelineFilters filters, CancellationToken ct)
    {
        // Aggregate by date in SQL, then combine the small daily counts in memory.
        // This also avoids provider-specific DateOnly.Year/Month SQL translations.
        var days = await ApplyFilters(db.TimelineEntries.AsNoTracking(), ownerId, includeHidden, filters)
            .GroupBy(e => e.OccurredOn).Select(g => new { Date = g.Key, Count = g.Count() }).ToListAsync(ct);
        return new(days.GroupBy(d => d.Date.Year).OrderByDescending(g => g.Key)
            .Select(year => new TimelineYearDto(year.Key, year.Sum(d => d.Count),
                year.GroupBy(d => d.Date.Month).OrderByDescending(g => g.Key)
                    .Select(month => new TimelineMonthDto(month.Key, month.Sum(d => d.Count))).ToArray())).ToArray());
    }

    internal static string? Excerpt(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var runes = text.EnumerateRunes().ToArray();
        if (runes.Length <= limit) return text;
        var end = limit - 1;
        while (end > 0 && !Rune.IsWhiteSpace(runes[end])) end--;
        return string.Concat(runes.Take(end).Select(r => r.ToString())).TrimEnd() + "…";
    }
}
