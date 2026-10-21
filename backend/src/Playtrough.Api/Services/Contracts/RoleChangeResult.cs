using Playtrough.Api.Dtos.Admin;

namespace Playtrough.Api.Services.Contracts;

public enum RoleChangeError { InvalidRole, NotFound, SelfRoleChange, LastAdmin, InvalidRoleState, Unexpected }
public record RoleChangeResult(AdminUserDto? User, RoleChangeError? Error = null);
