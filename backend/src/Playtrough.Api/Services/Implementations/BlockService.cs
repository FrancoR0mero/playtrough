using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Playtrough.Api.Data;
using Playtrough.Api.Dtos.Blocks;
using Playtrough.Api.Dtos.Friends;
using Playtrough.Api.Models;
using Playtrough.Api.Services.Contracts;
using Playtrough.Api.Services.Interfaces;

namespace Playtrough.Api.Services.Implementations;

public sealed class BlockService(AppDbContext db, ILookupNormalizer normalizer, TimeProvider time) : IBlockService
{
    public async Task BlockAsync(int blockerId, string username, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        var other = await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalized &&
            (u.EmailConfirmed || db.UserBlocks.Any(b => b.BlockerId == blockerId && b.BlockedId == u.Id)))
            .Select(u => (int?)u.Id).SingleOrDefaultAsync(ct) ?? throw new ProfileException("user_not_found");
        if (other == blockerId) throw new ProfileException("cannot_block_self");
        await UserPairTransaction.RunAsync(db, blockerId, other, async () =>
        {
            // Recheck names and account visibility under the same locks as friendship writes.
            if (!await db.Users.AnyAsync(u => u.Id == other && u.NormalizedUserName == normalized &&
                (u.EmailConfirmed || db.UserBlocks.Any(b => b.BlockerId == blockerId && b.BlockedId == other)), ct))
                throw new ProfileException("user_not_found");
            if (!await db.UserBlocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == other, ct))
                db.UserBlocks.Add(new UserBlock { BlockerId = blockerId, BlockedId = other, CreatedAt = time.GetUtcNow().UtcDateTime });
            var low = Math.Min(blockerId, other);
            var high = Math.Max(blockerId, other);
            await db.Friendships.Where(f => f.UserLowId == low && f.UserHighId == high).ExecuteDeleteAsync(ct);
            var now = time.GetUtcNow().UtcDateTime;
            await db.FriendRequests.Where(r => r.Status == FriendRequestStatus.Pending &&
                ((r.SenderId == blockerId && r.ReceiverId == other) || (r.SenderId == other && r.ReceiverId == blockerId)))
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, FriendRequestStatus.Canceled).SetProperty(r => r.RespondedAt, now), ct);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task UnblockAsync(int blockerId, string username, CancellationToken ct = default)
    {
        var normalized = Normalize(username);
        var other = await db.Users.AsNoTracking().Where(u => u.NormalizedUserName == normalized)
            .Select(u => (int?)u.Id).SingleOrDefaultAsync(ct);
        if (other is null) return;
        if (other == blockerId) throw new ProfileException("cannot_block_self");
        await UserPairTransaction.RunAsync(db, blockerId, other.Value, async () =>
        {
            await db.UserBlocks.Where(b => b.BlockerId == blockerId && b.BlockedId == other).ExecuteDeleteAsync(ct);
            return true;
        }, ct);
    }

    public async Task<BlockedUsersPage> ListAsync(int blockerId, FriendsPageRequest page, CancellationToken ct = default)
    {
        if (page.Page < 1 || page.PageSize is < 1 or > 50 || ((long)page.Page - 1) * page.PageSize > int.MaxValue)
            throw new ProfileException("invalid_query");
        var rows = await (from block in db.UserBlocks.AsNoTracking()
            join user in db.Users on block.BlockedId equals user.Id
            where block.BlockerId == blockerId
            orderby user.NormalizedUserName, user.Id
            select new { user.UserName, user.AvatarFileName, block.CreatedAt })
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize + 1).ToListAsync(ct);
        return new(rows.Take(page.PageSize).Select(r => new BlockedUserDto(new(r.UserName!, AvatarUrls.FromFile(r.AvatarFileName)), r.CreatedAt)).ToArray(),
            page.Page, page.PageSize, rows.Count > page.PageSize);
    }

    private string Normalize(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 50) throw new ProfileException("invalid_query");
        return normalizer.NormalizeName(username.Trim());
    }
}
