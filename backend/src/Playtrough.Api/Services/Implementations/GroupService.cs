using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Hubs;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class GroupService(AppDbContext db, GroupAccess access, TimeProvider clock, ChatConnections chat)
{
    internal async Task<GroupDto> CreateAsync(int ownerId, string name, string description, string topic,
        GroupPrivacy privacy, GroupClassificationSelection classification, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var group = new SocialGroup
        {
            OwnerId = ownerId, Name = name, Description = description, Topic = topic,
            GenreId = classification.GenreId, Privacy = privacy, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        };
        group.Subgenres.AddRange(classification.SubgenreIds.Select(id => new GroupSubgenreAssignment { SubgenreId = id }));
        group.Tags.AddRange(classification.TagIds.Select(id => new GroupTagAssignment { TagId = id }));
        db.GroupMembers.Add(new GroupMember { Group = group, UserId = ownerId, JoinedAt = now });
        await db.SaveChangesAsync(ct);
        return ToDto(await WithClassification(db.SocialGroups.AsNoTracking()).SingleAsync(item => item.Id == group.Id, ct));
    }

    internal async Task<GroupPageDto> ListAsync(string? text, string? topic, GroupClassificationSelection classification,
        int? cursor, int limit, CancellationToken ct)
    {
        var groups = access.ActiveGroups();
        if (text is not null)
        {
            var pattern = "%" + LikePattern.Escape(text.ToUpperInvariant()) + "%";
            groups = groups.Where(group =>
                EF.Functions.Like(group.Name.ToUpper(), pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.Like(group.Description.ToUpper(), pattern, LikePattern.EscapeCharacter));
        }
        if (topic is not null) groups = groups.Where(group => group.Topic == topic);
        if (classification.GenreId is { } genre) groups = groups.Where(group => group.GenreId == genre);
        foreach (var subgenre in classification.SubgenreIds)
            groups = groups.Where(group => group.Subgenres.Any(link => link.SubgenreId == subgenre));
        foreach (var tag in classification.TagIds)
            groups = groups.Where(group => group.Tags.Any(link => link.TagId == tag));
        if (cursor is { } before) groups = groups.Where(group => group.Id < before);

        var page = await WithClassification(groups.OrderByDescending(group => group.Id).Take(limit + 1)).ToListAsync(ct);
        var items = page.Take(limit).Select(ToDto).ToArray();
        return new GroupPageDto(items, page.Count > limit ? items[^1].Id : null);
    }

    public async Task<(GroupProfileDto Profile, Guid? Version)?> FindAsync(int id, int viewer, CancellationToken ct)
    {
        var row = await WithClassification(access.ActiveGroups()).Where(group => group.Id == id).Select(group => new
        {
            Group = group,
            Active = access.ActiveMembers().Any(member => member.GroupId == group.Id && member.UserId == viewer),
            Removed = db.GroupMembers.Any(member => member.GroupId == group.Id && member.UserId == viewer && member.RemovedAt != null),
            Request = db.GroupJoinRequests.Where(request => request.GroupId == group.Id && request.UserId == viewer)
                .OrderByDescending(request => request.Id).Select(request => (GroupJoinRequestStatus?)request.Status).FirstOrDefault(),
        }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var group = ToDto(row.Group);
        var status = row.Group.OwnerId == viewer ? "owner" : row.Active ? "member" :
            row.Request == GroupJoinRequestStatus.Pending ? "pending" :
            row.Request == GroupJoinRequestStatus.Rejected ? "rejected" : row.Removed ? "removed" : "none";
        var profile = new GroupProfileDto(group.Id, group.Name, group.Description, group.Topic, group.Privacy, status)
        {
            Genre = group.Genre, Subgenres = group.Subgenres, Tags = group.Tags,
        };
        return (profile, status == "owner" ? row.Group.ConcurrencyToken : null);
    }

    public Task<SocialGroup?> FindOwnedAsync(int id, int owner, CancellationToken ct)
        => access.OwnedGroups(owner).SingleOrDefaultAsync(group => group.Id == id, ct);

    internal Task<bool> HasSubgenresAsync(int id, CancellationToken ct)
        => db.GroupSubgenreAssignments.AnyAsync(link => link.GroupId == id, ct);

    internal async Task<bool> UpdateAsync(int id, int owner, Guid version, string name, string description,
        string topic, GroupPrivacy privacy, GroupClassificationSelection? classification, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var next = Guid.NewGuid();
        var changed = await CurrentVersion(id, owner, version).ExecuteUpdateAsync(update =>
        {
            update
                .SetProperty(group => group.Name, name)
                .SetProperty(group => group.Description, description)
                .SetProperty(group => group.Topic, topic)
                .SetProperty(group => group.Privacy, privacy)
                .SetProperty(group => group.ConcurrencyToken, next);
            if (classification is not null)
                update.SetProperty(group => group.GenreId, classification.GenreId);
        }, ct);
        if (changed != 1) return false;

        if (classification is not null)
        {
            await db.GroupSubgenreAssignments.Where(link => link.GroupId == id).ExecuteDeleteAsync(ct);
            await db.GroupTagAssignments.Where(link => link.GroupId == id).ExecuteDeleteAsync(ct);
            db.GroupSubgenreAssignments.AddRange(classification.SubgenreIds.Select(subgenreId =>
                new GroupSubgenreAssignment { GroupId = id, SubgenreId = subgenreId }));
            db.GroupTagAssignments.AddRange(classification.TagIds.Select(tagId =>
                new GroupTagAssignment { GroupId = id, TagId = tagId }));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public Task<bool> DeleteAsync(int id, int owner, Guid version, CancellationToken ct)
        => chat.InGroupAsync(id, async () =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var next = Guid.NewGuid();
            if (await CurrentVersion(id, owner, version).ExecuteUpdateAsync(update => update
                .SetProperty(group => group.DeletedAt, now)
                .SetProperty(group => group.ConcurrencyToken, next), ct) != 1) return false;
            await chat.RevokeAsync(id);
            return true;
        }, ct);

    private IQueryable<SocialGroup> CurrentVersion(int id, int owner, Guid version)
        => access.OwnedGroups(owner).Where(group => group.Id == id && group.ConcurrencyToken == version);

    public async Task<GroupReviewPageDto?> ListReviewsAsync(int id, int viewer, int? cursor, int limit, CancellationToken ct)
    {
        var reviews = db.Reviews.AsNoTracking();
        if (cursor is { } before) reviews = reviews.Where(review => review.Id < before);

        var eligible = from member in access.ActiveMembers()
                       join author in PrivacyService.VisibleUsers(db, viewer) on member.UserId equals author.Id
                       join review in reviews on member.UserId equals review.UserId
                       join game in db.Games.AsNoTracking() on review.GameId equals game.Id
                       where member.GroupId == id
                       orderby review.Id descending
                       select new { member.GroupId, review.Id, review.GameId, GameName = game.Name,
                           author.UserName, review.Rating, review.Text, review.CreatedAt, review.EditedAt };

        // A left join preserves an empty page for a member. Membership, review visibility
        // and pagination are evaluated in the same SQL statement.
        var page = await (from visible in access.MemberGroups(viewer).Where(item => item.Id == id)
                          join review in eligible.Take(limit + 1) on visible.Id equals review.GroupId into items
                          from review in items.DefaultIfEmpty()
                          orderby review.Id descending
                          select new { Id = (int?)review.Id, GameId = (int?)review.GameId, review.GameName,
                              review.UserName, Rating = (decimal?)review.Rating, review.Text,
                              CreatedAt = (DateTime?)review.CreatedAt, review.EditedAt }).ToListAsync(ct);
        if (page.Count == 0) return null;
        var result = page.Where(row => row.Id.HasValue).Take(limit)
            .Select(row => new GroupReviewDto(row.Id!.Value, row.GameId!.Value, row.GameName!, row.UserName!,
                row.Rating!.Value, row.Text, row.CreatedAt!.Value, row.EditedAt)).ToArray();
        return new GroupReviewPageDto(result, page.Count > limit ? result[^1].Id : null);
    }

    private static IQueryable<SocialGroup> WithClassification(IQueryable<SocialGroup> groups)
        => groups.Include(group => group.Genre)
            .Include(group => group.Subgenres).ThenInclude(link => link.Subgenre)
            .Include(group => group.Tags).ThenInclude(link => link.Tag)
            .AsSingleQuery();

    private static GroupDto ToDto(SocialGroup group)
        => new(group.Id, group.Name, group.Description, group.Topic,
            group.Privacy == GroupPrivacy.Private ? "private" : "public")
        {
            Genre = group.Genre is { } genre ? new(genre.Id, genre.Name) : null,
            Subgenres = group.Subgenres.Select(link => link.Subgenre).OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => new GroupSubgenreDto(item.Id, item.Name, item.GenreId, item.Alias)).ToArray(),
            Tags = group.Tags.Select(link => link.Tag).OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => new GroupTagDto(item.Id, item.Name, item.Category)).ToArray(),
        };
}
