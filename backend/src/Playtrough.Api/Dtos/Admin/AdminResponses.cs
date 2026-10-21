namespace Playtrough.Api.Dtos.Admin;

public record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record AdminUserDto(int Id, string Username, string Email, string Role, bool EmailConfirmed);
public record RoleChangeDto(int Id, int ChangedByUserId, int TargetUserId, string OldRole, string NewRole, DateTime ChangedAt);
