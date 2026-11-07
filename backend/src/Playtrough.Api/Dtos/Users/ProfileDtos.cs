using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playtrough.Api.Dtos.Users;

public sealed record UserSummaryDto(string Username, string? AvatarUrl);
public sealed record RelationshipDto(string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RequestId = null);
public sealed record MyProfileDto(int Id, string Username, string? Email, string? AvatarUrl, string? Bio,
    DateTime CreatedAt, int FriendsCount, int PendingReceivedCount, DateTime? UsernameChangeAvailableAt);
public sealed record ProfileDto(string Username, string? AvatarUrl, string? Bio, DateTime CreatedAt,
    int FriendsCount, RelationshipDto Relationship, bool IsRestricted, string? Restriction);

// Undefined distinguishes omitted properties from explicit null in a partial update.
public sealed class UpdateProfileRequest
{
    public JsonElement Username { get; init; }
    public JsonElement Bio { get; init; }
}
