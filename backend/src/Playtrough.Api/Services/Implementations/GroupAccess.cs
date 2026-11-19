using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Models;

namespace Playtrough.Api.Services.Implementations;

public sealed class GroupAccess(AppDbContext db)
{
    internal IQueryable<SocialGroup> ActiveGroups() => db.SocialGroups.AsNoTracking().Where(group => group.DeletedAt == null);

    internal IQueryable<GroupMember> ActiveMembers() => db.GroupMembers.AsNoTracking().Where(member => member.RemovedAt == null);

    internal IQueryable<SocialGroup> MemberGroups(int userId)
        => ActiveGroups().Where(group => ActiveMembers().Any(member => member.GroupId == group.Id && member.UserId == userId));

    internal IQueryable<SocialGroup> OwnedGroups(int userId) => ActiveGroups().Where(group => group.OwnerId == userId);
}
