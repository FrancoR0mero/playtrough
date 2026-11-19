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

    public async Task<GroupProfileDto?> FindAsync(int id, int viewer, CancellationToken ct)
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
        return new GroupProfileDto(group.Id, group.Name, group.Description, group.Topic, group.Privacy, status);
    }

    private static IQueryable<GroupDto> Discovery(IQueryable<SocialGroup> groups)
        => groups.Select(group => new GroupDto(group.Id, group.Name, group.Description, group.Topic,
            group.Privacy == GroupPrivacy.Private ? "private" : "public"));

    private static GroupDto ToDto(SocialGroup group)
        => new(group.Id, group.Name, group.Description, group.Topic,
            group.Privacy == GroupPrivacy.Private ? "private" : "public");
}
