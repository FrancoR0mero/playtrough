using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class GroupService(AppDbContext db, GroupAccess access, TimeProvider clock)
{
    public async Task<GroupDto> CreateAsync(int ownerId, string name, string description, string topic,
        GroupPrivacy privacy, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var group = new SocialGroup
        {
            OwnerId = ownerId, Name = name, Description = description, Topic = topic,
            Privacy = privacy, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        };
        db.GroupMembers.Add(new GroupMember { Group = group, UserId = ownerId, JoinedAt = now });
        await db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    public async Task<GroupPageDto> ListAsync(string? text, string? topic, int? cursor, int limit, CancellationToken ct)
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
        if (cursor is { } before) groups = groups.Where(group => group.Id < before);

        var page = await Discovery(groups.OrderByDescending(group => group.Id).Take(limit + 1)).ToListAsync(ct);
        var items = page.Take(limit).ToArray();
        return new GroupPageDto(items, page.Count > limit ? items[^1].Id : null);
    }

    public async Task<(GroupProfileDto Profile, Guid? Version)?> FindAsync(int id, int viewer, CancellationToken ct)
    {
        var row = await access.ActiveGroups().Where(group => group.Id == id).Select(group => new
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
        var profile = new GroupProfileDto(group.Id, group.Name, group.Description, group.Topic, group.Privacy, status);
        return (profile, status == "owner" ? row.Group.ConcurrencyToken : null);
    }

    public Task<SocialGroup?> FindOwnedAsync(int id, int owner, CancellationToken ct)
        => access.OwnedGroups(owner).SingleOrDefaultAsync(group => group.Id == id, ct);

    public async Task<bool> UpdateAsync(int id, int owner, Guid version, string name, string description,
        string topic, GroupPrivacy privacy, CancellationToken ct)
    {
        var next = Guid.NewGuid();
        return await CurrentVersion(id, owner, version).ExecuteUpdateAsync(update => update
            .SetProperty(group => group.Name, name)
            .SetProperty(group => group.Description, description)
            .SetProperty(group => group.Topic, topic)
            .SetProperty(group => group.Privacy, privacy)
            .SetProperty(group => group.ConcurrencyToken, next), ct) == 1;
    }

    public async Task<bool> DeleteAsync(int id, int owner, Guid version, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var next = Guid.NewGuid();
        return await CurrentVersion(id, owner, version).ExecuteUpdateAsync(update => update
            .SetProperty(group => group.DeletedAt, now)
            .SetProperty(group => group.ConcurrencyToken, next), ct) == 1;
    }

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

    private static IQueryable<GroupDto> Discovery(IQueryable<SocialGroup> groups)
        => groups.Select(group => new GroupDto(group.Id, group.Name, group.Description, group.Topic,
            group.Privacy == GroupPrivacy.Private ? "private" : "public"));

    private static GroupDto ToDto(SocialGroup group)
        => new(group.Id, group.Name, group.Description, group.Topic,
            group.Privacy == GroupPrivacy.Private ? "private" : "public");
}
