using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Groups;
using Playtrough.Api.Services.Implementations;

namespace Playtrough.Api.Hubs;

public sealed class ChatHistoryService(AppDbContext db, GroupAccess access)
{
    public async Task<ChatHistoryPageDto?> ListAsync(int groupId, int viewer, long? before, long? after,
        long? through, int limit, CancellationToken ct)
    {
        var visible = from message in db.GroupMessages.AsNoTracking()
                      join author in PrivacyService.UnblockedUsers(db, viewer) on message.AuthorId equals author.Id
                      where message.GroupId == groupId
                      select new { Message = message, author.UserName };
        var eligible = visible;
        if (before is { } older) eligible = eligible.Where(row => row.Message.Id < older);
        if (after is { } newer) eligible = eligible.Where(row => row.Message.Id > newer);
        if (through is { } upper) eligible = eligible.Where(row => row.Message.Id <= upper);

        var page = after is null ? eligible.OrderByDescending(row => row.Message.Id) : eligible.OrderBy(row => row.Message.Id);
        // Membership, page and catch-up bound all come from the same statement.
        var result = await (from groupRow in access.MemberGroups(viewer).Where(item => item.Id == groupId)
                            join row in page.Take(limit + 1) on groupRow.Id equals row.Message.GroupId into rows
                            from row in rows.DefaultIfEmpty()
                            select new
                            {
                                row.Message, row.UserName,
                                Latest = after != null && through == null
                                    ? visible.OrderByDescending(item => item.Message.Id).Select(item => item.Message.Id).FirstOrDefault()
                                    : 0L,
                            }).ToListAsync(ct);
        if (result.Count == 0) return null;

        var present = result.Where(row => row.Message is not null);
        var ordered = after is null ? present.OrderByDescending(row => row.Message.Id) : present.OrderBy(row => row.Message.Id);
        var items = ordered.Take(limit).Select(row => ChatMessageService.ToDto(row.Message, row.UserName!)).ToArray();
        var next = result.Count > limit ? items[^1].Id : null;
        string? bound = before is not null ? null : after is { } checkpoint
            ? Cursor(through ?? Math.Max(checkpoint, result[0].Latest))
            : items.FirstOrDefault()?.Id ?? "0";
        if (after is null) Array.Reverse(items);
        return new ChatHistoryPageDto(items, next, bound);
    }

    private static string Cursor(long id) => id.ToString(CultureInfo.InvariantCulture);
}
