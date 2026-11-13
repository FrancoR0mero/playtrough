using Playtrough.Api.Dtos.Blocks;
using Playtrough.Api.Dtos.Friends;

namespace Playtrough.Api.Services.Interfaces;

public interface IBlockService
{
    Task BlockAsync(int blockerId, string username, CancellationToken ct = default);
    Task UnblockAsync(int blockerId, string username, CancellationToken ct = default);
    Task<BlockedUsersPage> ListAsync(int blockerId, FriendsPageRequest page, CancellationToken ct = default);
}
