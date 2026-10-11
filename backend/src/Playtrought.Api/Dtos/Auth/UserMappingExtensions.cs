using Playtrought.Api.Models;

namespace Playtrought.Api.Dtos.Auth;

/// <summary>Static mapping helpers for user DTOs (no AutoMapper).</summary>
public static class UserMappingExtensions
{
    /// <summary>Maps a persisted user to its public registration response.</summary>
    public static RegisterResponseDto ToRegisterResponse(this ApplicationUser user) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, user.CreatedAt);
}