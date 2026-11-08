namespace Playtrough.Api.Services.Contracts;

public sealed class ProfileException(string code, DateTime? availableAt = null) : Exception(code)
{
    public string Code { get; } = code;
    public DateTime? AvailableAt { get; } = availableAt;
}
