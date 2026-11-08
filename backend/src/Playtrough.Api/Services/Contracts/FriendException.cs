namespace Playtrough.Api.Services.Contracts;

public sealed class FriendException(string code, DateTime? availableAt = null, int? requestId = null) : Exception(code)
{
    public string Code { get; } = code;
    public DateTime? AvailableAt { get; } = availableAt;
    public int? RequestId { get; } = requestId;
}
