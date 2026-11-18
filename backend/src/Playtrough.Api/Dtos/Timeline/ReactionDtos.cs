using System.Text.Json;

namespace Playtrough.Api.Dtos.Timeline;

public sealed record ReactionSummaryDto(string? MyReaction, int LikeCount, int DislikeCount);

public sealed class SetReactionRequest
{
    public JsonElement Value { get; init; }
}
