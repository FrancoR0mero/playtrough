namespace Playtrough.Api.Services.Contracts;

public sealed class TimelineException(string code, string? restriction = null) : Exception(code)
{
    public string Code { get; } = code;
    public string? Restriction { get; } = restriction;
}
