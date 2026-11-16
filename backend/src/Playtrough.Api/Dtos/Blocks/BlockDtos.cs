using System.Text.Json;
using Playtrough.Api.Dtos.Users;

namespace Playtrough.Api.Dtos.Blocks;

public sealed class BlockUserRequest { public JsonElement Username { get; init; } }
public sealed record BlockedUserDto(UserSummaryDto User, DateTime BlockedAt);
public sealed record BlockedUsersPage(IReadOnlyList<BlockedUserDto> Items, int Page, int PageSize, bool HasMore);
