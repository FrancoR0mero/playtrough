using System.ComponentModel.DataAnnotations;
using Playtrough.Api.Authorization;

namespace Playtrough.Api.Dtos.Admin;

public class PaginationRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 20;
}

public sealed class UserListRequest : PaginationRequest
{
    [StringLength(200)] public string? Search { get; init; }
    [RoleName] public string? Role { get; init; }
}

public sealed class RoleChangeListRequest : PaginationRequest
{
    [Range(1, int.MaxValue)] public int? TargetUserId { get; init; }
}

public sealed class ChangeRoleRequest
{
    [Required, RoleName] public string Role { get; init; } = string.Empty;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class RoleNameAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || value is string role && AppRoles.IsValid(role);
    public override string FormatErrorMessage(string name) => $"{name} must be one of: {string.Join(", ", AppRoles.All)}.";
}
